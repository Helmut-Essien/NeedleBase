using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Reports;
using NeedleBase.Application.Sync;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Domain.Exceptions;

namespace NeedleBase.Application.Expenses;

/// <summary>
/// Expenses use cases (plans step 7b-2). Order of checks: plan (Workshop/Production/complimentary/keyless →
/// <c>EXPENSES_NOT_ALLOWED</c>), role (Owner/Manager → <c>EXPENSES_FORBIDDEN</c>), then validation and existence.
/// Tenant-filtered sets: another shop's ids read as not found. Append-only; voiding is one-way and idempotent.
/// </summary>
public sealed class ExpenseService : IExpenseService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IPlanAccess _plans;

    public ExpenseService(IApplicationDbContext db, ICurrentUser currentUser, IClock clock, IPlanAccess plans)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _plans = plans;
    }

    public async Task<RecordResult<ExpenseListResponse>> ListAsync(ExpenseListQuery query, CancellationToken cancellationToken = default)
    {
        if (await AccessAsync(cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return RecordResult.Fail(denied);
        }

        ArgumentNullException.ThrowIfNull(query);
        var expenses = _db.Expenses.AsNoTracking();
        var orderId = string.IsNullOrWhiteSpace(query.OrderId) ? null : query.OrderId.Trim();
        string? monthKey = null;
        if (orderId is null || !string.IsNullOrWhiteSpace(query.Month))
        {
            var today = BusinessTime.Today(_clock.UtcNow);
            if (!ReportRules.TryResolveMonth(query.Month, today, out var month))
            {
                return RecordResult.Fail(RecordError.ExpenseFilterInvalid, "That month isn't available. Pick a month from January 2020 to this month.");
            }

            var next = month.AddMonths(1);
            expenses = expenses.Where(e => e.SpentOn >= month && e.SpentOn < next);
            monthKey = ReportRules.Key(month);
        }

        if (orderId is not null)
        {
            expenses = expenses.Where(e => e.OrderId == orderId);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            if (!ExpenseCategory.IsValid(category))
            {
                return RecordResult.Fail(RecordError.ExpenseFilterInvalid, "Pick a category from the list.");
            }

            expenses = expenses.Where(e => e.Category == category);
        }

        // Totals cover the whole filter; voided expenses never count, even when they're listed.
        var totals = await expenses.Where(e => !e.IsVoided)
            .GroupBy(e => e.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(e => e.Amount), Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var byCategory = totals
            .OrderByDescending(t => t.Total).ThenBy(t => ExpenseCategory.All.ToList().IndexOf(t.Category))
            .Select(t => new CategoryTotalDto(t.Category, t.Total, t.Count))
            .ToList();

        var listed = query.IncludeVoided ? expenses : expenses.Where(e => !e.IsVoided);
        var (page, pageSize) = PaginatedResponse<ExpenseDto>.Normalize(query.Page, query.PageSize);
        var totalCount = await listed.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await listed
            .OrderByDescending(e => e.SpentOn).ThenByDescending(e => e.CreatedAtUtc).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var orders = await OrderLabelsAsync(rows.Select(r => r.OrderId), cancellationToken).ConfigureAwait(false);
        var data = rows.Select(r => ToDto(r, orders)).ToList();

        return new ExpenseListResponse(
            PaginatedResponse<ExpenseDto>.Create(data, totalCount, page, pageSize),
            byCategory.Sum(c => c.Total),
            byCategory.Sum(c => c.Count),
            byCategory,
            monthKey);
    }

    public async Task<RecordResult<ExpenseDto>> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (await AccessAsync(cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return RecordResult.Fail(denied);
        }

        var entity = await _db.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
        return entity is null
            ? RecordResult.Fail(RecordError.ExpenseNotFound)
            : await ToDtoAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RecordResult<ExpenseSaveResult>> RecordAsync(RecordExpenseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await AccessAsync(cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return RecordResult.Fail(denied);
        }

        // Sync push already runs each op in a transaction; REST gets its own so the locks hold to commit.
        return _db.HasActiveTransaction
            ? await RecordLockedAsync(request, cancellationToken).ConfigureAwait(false)
            : await _db.InTransactionAsync(async ct =>
            {
                var result = await RecordLockedAsync(request, ct).ConfigureAwait(false);
                return (result, result.IsSuccess);
            }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RecordResult<ExpenseSaveResult>> RecordLockedAsync(RecordExpenseRequest request, CancellationToken cancellationToken)
    {
        var businessId = EnsureBusiness();
        var utcNow = _clock.UtcNow;
        var today = BusinessTime.Today(utcNow);
        var (error, detail, entry) = ExpenseRules.Check(request.Amount, request.Category, request.SpentOn, request.Note, today, request.Lenient);
        if (error != RecordError.None || entry is null)
        {
            return RecordResult.Fail(error == RecordError.None ? RecordError.ExpenseInvalid : error, detail);
        }

        var id = string.IsNullOrWhiteSpace(request.Id) ? IdGenerator.NewId() : request.Id.Trim();
        var orderId = string.IsNullOrWhiteSpace(request.OrderId) ? null : request.OrderId.Trim();

        // Serialise per expense id (replays) and per order (an order delete waits, then sees this expense).
        await _db.LockForExpenseAsync(id, orderId, cancellationToken).ConfigureAwait(false);

        // Ids are global (another shop's row is invisible through the tenant filter but still owns the key).
        var existing = await _db.Expenses.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            // A sync replay of an expense whose order had gone: stored without the link, the note marked.
            var detachedNote = entry.Note is null ? Expense.OrderRemovedMarker : entry.Note + " " + Expense.OrderRemovedMarker;
            var detached = request.Lenient && orderId is not null && existing.OrderId is null && existing.Note == detachedNote;
            var same = existing.BusinessId == businessId && existing.Amount == entry.Amount &&
                       existing.Category == entry.Category && existing.SpentOn == entry.SpentOn &&
                       ((existing.OrderId == orderId && existing.Note == entry.Note) || detached);
            return same
                ? new ExpenseSaveResult(await ToDtoAsync(existing, cancellationToken).ConfigureAwait(false), false)
                : RecordResult.Fail(RecordError.ExpenseConflict);
        }

        var orderMissing = orderId is not null &&
                           !await _db.Orders.AsNoTracking().AnyAsync(o => o.Id == orderId, cancellationToken).ConfigureAwait(false);
        if (orderMissing && !request.Lenient)
        {
            return RecordResult.Fail(RecordError.ExpenseOrderInvalid);
        }

        var now = SyncTime.Normalize(utcNow); // µs, as stored: responses match later reads
        Expense entity;
        try
        {
            entity = Expense.Create(id, businessId, entry.Amount, entry.Category, entry.SpentOn, entry.Note, orderId,
                _currentUser.UserId, today, now);
            if (orderMissing)
            {
                entity.DetachOrder(now); // sync: the order is gone; keep the money record, drop the link
            }
        }
        catch (DomainRuleException ex)
        {
            return RecordResult.Fail(RecordError.ExpenseInvalid, ex.Message);
        }
        catch (ArgumentException)
        {
            return RecordResult.Fail(RecordError.ExpenseInvalid);
        }

        _db.Expenses.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ExpenseSaveResult(await ToDtoAsync(entity, cancellationToken).ConfigureAwait(false), true);
    }

    public async Task<RecordResult<ExpenseDto>> VoidAsync(string id, VoidExpenseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await AccessAsync(cancellationToken).ConfigureAwait(false) is { } denied)
        {
            return RecordResult.Fail(denied);
        }

        // Two voids at once (REST + sync): serialised, so the first reason really wins.
        return _db.HasActiveTransaction
            ? await VoidLockedAsync(id, request, cancellationToken).ConfigureAwait(false)
            : await _db.InTransactionAsync(async ct =>
            {
                var result = await VoidLockedAsync(id, request, ct).ConfigureAwait(false);
                return (result, result.IsSuccess);
            }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RecordResult<ExpenseDto>> VoidLockedAsync(string id, VoidExpenseRequest request, CancellationToken cancellationToken)
    {
        await _db.LockForExpenseAsync(id, null, cancellationToken).ConfigureAwait(false);
        var entity = await _db.Expenses.FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return RecordResult.Fail(RecordError.ExpenseNotFound);
        }

        if (entity.IsVoided)
        {
            return await ToDtoAsync(entity, cancellationToken).ConfigureAwait(false); // idempotent; the first reason/time win
        }

        if (request.BaseModifiedUtc is { } baseModified && !SyncTime.SameInstant(baseModified, entity.DateModifiedUtc))
        {
            return RecordResult.Fail(RecordError.Stale);
        }

        if (ExpenseRules.CheckVoidReason(request.Reason, request.Lenient) is { } reasonError)
        {
            return RecordResult.Fail(RecordError.ExpenseInvalid, reasonError);
        }

        try
        {
            entity.Void(request.Reason, _currentUser.UserId, SyncTime.Normalize(_clock.UtcNow));
        }
        catch (ArgumentException ex)
        {
            return RecordResult.Fail(RecordError.ExpenseInvalid, ex.Message);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToDtoAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Null when allowed; otherwise the plan or role error (plan first).</summary>
    private async Task<RecordError?> AccessAsync(CancellationToken cancellationToken)
    {
        var businessId = EnsureBusiness();
        var entitlements = await _plans.GetEntitlementsAsync(businessId, cancellationToken).ConfigureAwait(false);
        if (entitlements?.ProfitAndMonthlyReports != true)
        {
            return RecordError.ExpensesNotAllowed;
        }

        return ExpenseRules.CanManage(_currentUser.Role) ? null : RecordError.ExpenseForbidden;
    }

    private async Task<Dictionary<string, (string DisplayRef, string CustomerName)>> OrderLabelsAsync(
        IEnumerable<string?> orderIds, CancellationToken cancellationToken)
    {
        var ids = orderIds.Where(id => id is not null).Select(id => id!).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await (
                from o in _db.Orders.AsNoTracking()
                join c in _db.Customers.AsNoTracking() on o.CustomerId equals c.Id
                where ids.Contains(o.Id)
                select new { o.Id, o.DisplayRef, c.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(r => r.Id, r => (r.DisplayRef, r.Name), StringComparer.Ordinal);
    }

    private async Task<ExpenseDto> ToDtoAsync(Expense e, CancellationToken cancellationToken) =>
        ToDto(e, await OrderLabelsAsync([e.OrderId], cancellationToken).ConfigureAwait(false));

    private static ExpenseDto ToDto(Expense e, IReadOnlyDictionary<string, (string DisplayRef, string CustomerName)> orders)
    {
        (string DisplayRef, string CustomerName)? label = e.OrderId is not null && orders.TryGetValue(e.OrderId, out var l) ? l : null;
        return new ExpenseDto(
            e.Id, e.Amount, e.Category, e.SpentOn, e.Note, e.OrderId, label?.DisplayRef, label?.CustomerName,
            e.RecordedByUserId, e.IsVoided, e.VoidedAtUtc, e.VoidedByUserId, e.VoidReason, e.CreatedAtUtc, e.DateModifiedUtc);
    }

    private string EnsureBusiness()
    {
        if (string.IsNullOrEmpty(_currentUser.BusinessId))
        {
            throw new InvalidOperationException("Business context is required.");
        }

        return _currentUser.BusinessId;
    }
}
