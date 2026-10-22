using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Exceptions;

namespace NeedleBase.Application.Payments;

/// <summary>Payments use-cases (tenant-filtered DbSets: another business's ids read as not found).</summary>
public sealed class PaymentService : IPaymentService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public PaymentService(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<RecordResult<OrderPaymentsDto>> GetForOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        if (!PaymentRules.CanManage(_currentUser.Role))
        {
            return RecordResult.Fail(RecordError.PaymentForbidden); // payments are hidden from tailors
        }

        var order = await _db.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.Id, o.Status, o.TotalAmount })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return RecordResult.Fail(RecordError.OrderNotFound);
        }

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.OrderId == orderId)
            .OrderByDescending(p => p.RecordedAtUtc).ThenByDescending(p => p.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var dtos = payments.Select(ToDto).ToList();
        return new OrderPaymentsDto(order.Id, order.Status, order.TotalAmount,
            PaymentRules.Paid(dtos.Select(p => (p.Amount, p.IsVoided))), dtos);
    }

    public async Task<RecordResult<PaginatedResponse<PaymentListItemDto>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        if (!PaymentRules.CanManage(_currentUser.Role))
        {
            return RecordResult.Fail(RecordError.PaymentForbidden);
        }

        (page, pageSize) = PaginatedResponse<PaymentListItemDto>.Normalize(page, pageSize);
        var query =
            from p in _db.Payments.AsNoTracking()
            join o in _db.Orders.AsNoTracking() on p.OrderId equals o.Id
            join c in _db.Customers.AsNoTracking() on p.CustomerId equals c.Id
            select new { Payment = p, o.DisplayRef, CustomerName = c.Name };

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(x => x.Payment.RecordedAtUtc).ThenByDescending(x => x.Payment.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var data = rows.Select(x => new PaymentListItemDto(
            x.Payment.Id, x.Payment.OrderId, x.DisplayRef, x.Payment.CustomerId, x.CustomerName,
            x.Payment.Amount, x.Payment.Method, x.Payment.RecordedAtUtc, x.Payment.IsVoided, x.Payment.DateModifiedUtc)).ToList();
        return PaginatedResponse<PaymentListItemDto>.Create(data, total, page, pageSize);
    }

    public async Task<RecordResult<PaymentDto>> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        if (!PaymentRules.CanManage(_currentUser.Role))
        {
            return RecordResult.Fail(RecordError.PaymentForbidden);
        }

        var entity = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
        return entity is null ? RecordResult.Fail(RecordError.PaymentNotFound) : ToDto(entity);
    }

    public async Task<RecordResult<PaymentDto>> RecordAsync(
        string orderId, RecordPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var businessId = EnsureBusiness();
        if (!PaymentRules.CanManage(_currentUser.Role))
        {
            return RecordResult.Fail(RecordError.PaymentForbidden);
        }

        var order = await _db.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.Id, o.CustomerId, o.Status, o.TotalAmount })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return RecordResult.Fail(RecordError.OrderNotFound);
        }

        var id = string.IsNullOrWhiteSpace(request.Id) ? IdGenerator.NewId() : request.Id.Trim();
        var method = request.Method?.Trim() ?? string.Empty;
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        // Ids are global (another business's row is invisible through the tenant filter but still owns the key).
        var existing = await _db.Payments.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            var same = existing.BusinessId == businessId && existing.OrderId == orderId &&
                       existing.Amount == request.Amount && existing.Method == method && existing.Note == note;
            return same ? ToDto(existing) : RecordResult.Fail(RecordError.PaymentConflict);
        }

        var paid = await _db.Payments.AsNoTracking()
            .Where(p => p.OrderId == orderId && !p.IsVoided)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken).ConfigureAwait(false) ?? 0m;
        var (error, detail) = PaymentRules.CheckEntry(order.Status, order.TotalAmount, paid, request.Amount, method, request.Lenient);
        if (error != RecordError.None)
        {
            return RecordResult.Fail(error, detail);
        }

        Payment entity;
        try
        {
            var now = Sync.SyncTime.Normalize(_clock.UtcNow); // µs, as stored: responses match later reads
            entity = Payment.Create(id, businessId, order.Id, order.CustomerId, request.Amount, method, note,
                Sync.SyncTime.Normalize(request.RecordedAtUtc ?? now), now);
        }
        catch (DomainRuleException ex)
        {
            return RecordResult.Fail(RecordError.PaymentInvalid, ex.Message);
        }
        catch (ArgumentException)
        {
            return RecordResult.Fail(RecordError.PaymentInvalid);
        }

        _db.Payments.Add(entity);
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Lost an insert race on the same id: same answer as the replay check above.
            _db.ChangeTracker.Clear();
            var raced = await _db.Payments.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
            if (raced is not null && raced.BusinessId == businessId && raced.OrderId == orderId &&
                raced.Amount == request.Amount && raced.Method == method && raced.Note == note)
            {
                return ToDto(raced);
            }

            if (raced is not null)
            {
                return RecordResult.Fail(RecordError.PaymentConflict);
            }

            throw;
        }

        return ToDto(entity);
    }

    public async Task<RecordResult<PaymentDto>> VoidAsync(string id, VoidPaymentRequest request, CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        if (!PaymentRules.CanManage(_currentUser.Role))
        {
            return RecordResult.Fail(RecordError.PaymentForbidden);
        }

        var entity = await _db.Payments.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return RecordResult.Fail(RecordError.PaymentNotFound);
        }

        if (entity.IsVoided)
        {
            return ToDto(entity); // idempotent; the first reason/time win
        }

        if (request.BaseModifiedUtc is { } baseModified && !SyncTimeEquals(baseModified, entity.DateModifiedUtc))
        {
            return RecordResult.Fail(RecordError.Stale);
        }

        try
        {
            entity.Void(request.Reason, Sync.SyncTime.Normalize(_clock.UtcNow));
        }
        catch (ArgumentException ex)
        {
            return RecordResult.Fail(RecordError.PaymentInvalid, ex.Message);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entity);
    }

    public static PaymentDto ToDto(Payment p) => new(
        p.Id, p.OrderId, p.CustomerId, p.Amount, p.Method, p.Note, p.RecordedAtUtc,
        p.IsVoided, p.VoidedAtUtc, p.VoidReason, p.CreatedAtUtc, p.DateModifiedUtc);

    private static bool SyncTimeEquals(DateTimeOffset a, DateTimeOffset b) => Sync.SyncTime.SameInstant(a, b);

    private string EnsureBusiness()
    {
        if (string.IsNullOrEmpty(_currentUser.BusinessId))
        {
            throw new InvalidOperationException("Business context is required.");
        }

        return _currentUser.BusinessId;
    }
}
