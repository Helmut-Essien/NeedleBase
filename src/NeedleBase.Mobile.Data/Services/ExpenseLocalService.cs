using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Application.Expenses;
using NeedleBase.Application.Reports;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Domain.Exceptions;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Sync;

namespace NeedleBase.Mobile.Data.Services;

public sealed class ExpenseLocalService : IExpenseLocalService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Func<DateTimeOffset> _utcNow;

    public ExpenseLocalService(IServiceScopeFactory scopeFactory)
        : this(scopeFactory, () => DateTimeOffset.UtcNow)
    {
    }

    public ExpenseLocalService(IServiceScopeFactory scopeFactory, Func<DateTimeOffset> utcNow)
    {
        _scopeFactory = scopeFactory;
        _utcNow = utcNow;
    }

    public async Task<RecordResult<ExpenseDto>> RecordAsync(string businessId, RecordExpenseRequest request, string? userId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentNullException.ThrowIfNull(request);
        if (PlanSaysNo(businessId))
        {
            return RecordResult.Fail(RecordError.ExpensesNotAllowed, ExpenseRules.NotAllowedMessage);
        }

        var utcNow = _utcNow();
        var today = BusinessTime.Today(utcNow);
        var (error, detail, entry) = ExpenseRules.Check(request.Amount, request.Category, request.SpentOn, request.Note, today, lenient: false);
        if (error != RecordError.None || entry is null)
        {
            return RecordResult.Fail(error == RecordError.None ? RecordError.ExpenseInvalid : error, detail);
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var id = string.IsNullOrWhiteSpace(request.Id) ? IdGenerator.NewId() : request.Id.Trim();
        var orderId = string.IsNullOrWhiteSpace(request.OrderId) ? null : request.OrderId.Trim();

        var existing = await db.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            // Double tap / retry with the same id: same answer, no second expense.
            var same = existing.BusinessId == businessId && existing.Amount == entry.Amount && existing.Category == entry.Category &&
                       existing.SpentOn == entry.SpentOn && existing.OrderId == orderId && existing.Note == entry.Note;
            return same
                ? await ToDtoAsync(db, existing, cancellationToken).ConfigureAwait(false)
                : RecordResult.Fail(RecordError.ExpenseConflict);
        }

        if (orderId is not null &&
            !await db.Orders.AsNoTracking().AnyAsync(o => o.BusinessId == businessId && o.Id == orderId, cancellationToken).ConfigureAwait(false))
        {
            return RecordResult.Fail(RecordError.ExpenseOrderInvalid);
        }

        try
        {
            var entity = Expense.Create(id, businessId, entry.Amount, entry.Category, entry.SpentOn, entry.Note, orderId, userId, today, utcNow);
            db.Expenses.Add(entity);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // queues the sync item in the same save
            return await ToDtoAsync(db, entity, cancellationToken).ConfigureAwait(false);
        }
        catch (SubscriptionReadOnlyException)
        {
            return RecordResult.Fail(RecordError.SubscriptionReadOnly, NeedleBase.Application.Licensing.LicenseErrors.ReadOnlyMessage);
        }
        catch (Exception ex) when (LocalStorageErrors.IsStorageFull(ex))
        {
            return RecordResult.Fail(RecordError.StorageFull, LocalStorageErrors.StorageFullMessage);
        }
        catch (DomainRuleException ex)
        {
            return RecordResult.Fail(RecordError.ExpenseInvalid, ex.Message);
        }
        catch (ArgumentException)
        {
            return RecordResult.Fail(RecordError.ExpenseInvalid);
        }
    }

    public async Task<RecordResult<ExpenseDto>> VoidAsync(string businessId, string expenseId, string? reason, string? userId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var entity = await db.Expenses.FirstOrDefaultAsync(e => e.BusinessId == businessId && e.Id == expenseId, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return RecordResult.Fail(RecordError.ExpenseNotFound);
        }

        if (entity.IsVoided)
        {
            return await ToDtoAsync(db, entity, cancellationToken).ConfigureAwait(false); // idempotent; the first reason wins
        }

        if (ExpenseRules.CheckVoidReason(reason, lenient: false) is { } reasonError)
        {
            return RecordResult.Fail(RecordError.ExpenseInvalid, reasonError);
        }

        try
        {
            entity.Void(reason, userId, _utcNow());
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return await ToDtoAsync(db, entity, cancellationToken).ConfigureAwait(false);
        }
        catch (SubscriptionReadOnlyException)
        {
            return RecordResult.Fail(RecordError.SubscriptionReadOnly, NeedleBase.Application.Licensing.LicenseErrors.ReadOnlyMessage);
        }
        catch (Exception ex) when (LocalStorageErrors.IsStorageFull(ex))
        {
            return RecordResult.Fail(RecordError.StorageFull, LocalStorageErrors.StorageFullMessage);
        }
        catch (ArgumentException ex)
        {
            return RecordResult.Fail(RecordError.ExpenseInvalid, ex.Message);
        }
    }

    public async Task<RecordResult<LocalExpenseList>> ListAsync(string businessId, ExpenseListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentNullException.ThrowIfNull(query);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var expenses = db.Expenses.AsNoTracking().Where(e => e.BusinessId == businessId);
        var orderId = string.IsNullOrWhiteSpace(query.OrderId) ? null : query.OrderId.Trim();
        string? monthKey = null;
        if (orderId is null || !string.IsNullOrWhiteSpace(query.Month))
        {
            if (!ReportRules.TryResolveMonth(query.Month, BusinessTime.Today(_utcNow()), out var month))
            {
                return RecordResult.Fail(RecordError.ExpenseFilterInvalid, "That month isn't available. Pick a month from January 2020 to this month.");
            }

            var next = month.AddMonths(1);
            expenses = expenses.Where(e => e.SpentOn >= month && e.SpentOn < next); // yyyy-MM-dd TEXT compares in date order
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

        // SQLite stores decimals as TEXT: load the month's rows and add up in C# (exact, no drift).
        var rows = await expenses.ToListAsync(cancellationToken).ConfigureAwait(false);
        var order = ExpenseCategory.All.ToList();
        var byCategory = rows.Where(e => !e.IsVoided)
            .GroupBy(e => e.Category, StringComparer.Ordinal)
            .Select(g => new CategoryTotalDto(g.Key, g.Sum(e => e.Amount), g.Count()))
            .OrderByDescending(t => t.Total).ThenBy(t => order.IndexOf(t.Category))
            .ToList();

        var listed = rows.Where(e => query.IncludeVoided || !e.IsVoided)
            .OrderByDescending(e => e.SpentOn).ThenByDescending(e => e.CreatedAtUtc).ThenByDescending(e => e.Id, StringComparer.Ordinal)
            .ToList();
        var (page, pageSize) = NeedleBase.Application.Common.PaginatedResponse<ExpenseDto>.Normalize(query.Page, query.PageSize);
        var pageRows = listed.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var items = await ToLocalAsync(db, businessId, pageRows, cancellationToken).ConfigureAwait(false);
        return new LocalExpenseList(items, byCategory.Sum(c => c.Total), byCategory.Sum(c => c.Count), byCategory, monthKey);
    }

    public async Task<LocalExpense?> GetAsync(string businessId, string expenseId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var entity = await db.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.BusinessId == businessId && e.Id == expenseId, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : (await ToLocalAsync(db, businessId, [entity], cancellationToken).ConfigureAwait(false))[0];
    }

    public async Task<int> PurgeHiddenAsync(string businessId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var queued = db.SyncQueue.Where(q => q.BusinessId == businessId && q.EntityType == SyncEntityType.Expense).Select(q => q.EntityId);
        var removed = await db.Expenses
            // Only copies the server confirmed: a row it never acknowledged is still the only record of that spending.
            .Where(e => e.BusinessId == businessId && EF.Property<bool>(e, LocalDbContext.ServerKnownColumn) && !queued.Contains(e.Id))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        if (removed > 0)
        {
            var device = await db.DeviceInfo.FirstOrDefaultAsync(d => d.BusinessId == businessId, cancellationToken).ConfigureAwait(false);
            if (device is not null)
            {
                device.ResyncPending = true;
                db.SuppressSyncQueue = true;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            LocalDataVersion.NotifyChangedBySync(businessId);
        }

        return removed;
    }

    /// <summary>The last pulled plan for this shop says expenses aren't included (unknown = allowed: the server decides).</summary>
    private static bool PlanSaysNo(string businessId) =>
        string.Equals(LocalSubscriptionGate.BusinessId, businessId, StringComparison.Ordinal) &&
        LocalSubscriptionGate.Current is { CanReports: false };

    private static async Task<ExpenseDto> ToDtoAsync(LocalDbContext db, Expense e, CancellationToken ct) =>
        (await ToLocalAsync(db, e.BusinessId, [e], ct).ConfigureAwait(false))[0].Expense;

    private static async Task<List<LocalExpense>> ToLocalAsync(LocalDbContext db, string businessId, IReadOnlyList<Expense> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var orderIds = rows.Where(r => r.OrderId is not null).Select(r => r.OrderId!).Distinct().ToList();
        var labels = orderIds.Count == 0
            ? new Dictionary<string, (string DisplayRef, string CustomerName)>(StringComparer.Ordinal)
            : (await (from o in db.Orders.AsNoTracking()
                        join c in db.Customers.AsNoTracking() on o.CustomerId equals c.Id
                        where o.BusinessId == businessId && orderIds.Contains(o.Id)
                        select new { o.Id, o.DisplayRef, c.Name })
                    .ToListAsync(ct).ConfigureAwait(false))
                .ToDictionary(r => r.Id, r => (r.DisplayRef, r.Name), StringComparer.Ordinal);
        var ids = rows.Select(r => r.Id).ToList();
        var pending = (await db.SyncQueue.AsNoTracking()
                .Where(q => q.BusinessId == businessId && q.EntityType == SyncEntityType.Expense && ids.Contains(q.EntityId))
                .Select(q => q.EntityId).ToListAsync(ct).ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);

        return rows.Select(e =>
        {
            (string DisplayRef, string CustomerName)? label = e.OrderId is not null && labels.TryGetValue(e.OrderId, out var l) ? l : null;
            var dto = new ExpenseDto(e.Id, e.Amount, e.Category, e.SpentOn, e.Note, e.OrderId, label?.DisplayRef, label?.CustomerName,
                e.RecordedByUserId, e.IsVoided, e.VoidedAtUtc, e.VoidedByUserId, e.VoidReason, e.CreatedAtUtc, e.DateModifiedUtc);
            return new LocalExpense(dto, pending.Contains(e.Id));
        }).ToList();
    }
}
