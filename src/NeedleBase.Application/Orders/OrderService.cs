using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Orders;

public sealed class OrderService : IOrderService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public OrderService(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<PaginatedResponse<OrderListItemDto>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? status,
        CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        (page, pageSize) = PaginatedResponse<OrderListItemDto>.Normalize(page, pageSize);
        var today = BusinessTime.Today(_clock.UtcNow);

        var query =
            from o in _db.Orders.AsNoTracking()
            join c in _db.Customers.AsNoTracking() on o.CustomerId equals c.Id
            select new { Order = o, CustomerName = c.Name, CustomerPhone = c.Phone };

        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusFilter = status.Trim();
            query = query.Where(x => x.Order.Status == statusFilter);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var refTerm = SearchText.Ref(search);
            var matching = CustomerSearchFilter.Apply(_db.Customers.AsNoTracking(), CustomerSearchKeys.Parse(search))
                .Select(c => c.Id);
            query = query.Where(x =>
                x.Order.DisplayRef.Contains(refTerm) ||
                matching.Contains(x.Order.CustomerId));
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(x => x.Order.CreatedAtUtc)
            .ThenByDescending(x => x.Order.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Order.Id,
                x.Order.DisplayRef,
                x.Order.CustomerId,
                x.CustomerName,
                x.Order.Status,
                x.Order.DueDate,
                x.Order.TotalAmount,
                x.Order.DateModifiedUtc
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows.Select(x => new OrderListItemDto(
            x.Id,
            x.DisplayRef,
            x.CustomerId,
            x.CustomerName,
            x.Status,
            x.DueDate,
            x.TotalAmount,
            x.DateModifiedUtc,
            OrderStatus.IsOverdue(x.Status, x.DueDate, today))).ToList();

        return PaginatedResponse<OrderListItemDto>.Create(items, total, page, pageSize);
    }

    public async Task<OrderDetailDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        var today = BusinessTime.Today(_clock.UtcNow);

        var row = await (
            from o in _db.Orders.AsNoTracking()
            join c in _db.Customers.AsNoTracking() on o.CustomerId equals c.Id
            where o.Id == id
            select new
            {
                Order = o,
                CustomerName = c.Name,
                CustomerPhone = c.Phone
            }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        var lines = await _db.OrderLines.AsNoTracking()
            .Where(l => l.OrderId == id)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return MapDetail(row.Order, row.CustomerName, row.CustomerPhone, lines, today);
    }

    public async Task<OrderResult> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var businessId = EnsureBusiness();
        var now = _clock.UtcNow;
        var id = string.IsNullOrWhiteSpace(request.Id) ? IdGenerator.NewId() : request.Id.Trim();

        // Idempotency is per order id (client ULID). Line ids are always server-generated.
        var existing = await _db.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return string.Equals(existing.BusinessId, businessId, StringComparison.Ordinal)
                ? OrderResult.Ok(await GetByIdAsync(id, cancellationToken).ConfigureAwait(false))
                : OrderResult.Fail(OrderError.IdConflict);
        }

        var customerExists = await _db.Customers.AsNoTracking()
            .AnyAsync(c => c.Id == request.CustomerId, cancellationToken)
            .ConfigureAwait(false);
        if (!customerExists)
        {
            return OrderResult.Fail(OrderError.CustomerNotFound);
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            return OrderResult.Fail(OrderError.Invalid, "An order needs at least one garment line.");
        }

        if (!await IsActiveMemberAsync(request.AssignedToUserId, cancellationToken).ConfigureAwait(false))
        {
            return OrderResult.Fail(OrderError.AssigneeInvalid);
        }

        try
        {
            var profileIds = OrderLineFactory.ProfileIds(request.Lines);
            var profilesById = profileIds.Count == 0
                ? new Dictionary<string, MeasurementProfile>(StringComparer.Ordinal)
                : await _db.MeasurementProfiles.AsNoTracking()
                    .Where(m => m.CustomerId == request.CustomerId && profileIds.Contains(m.Id))
                    .ToDictionaryAsync(m => m.Id, StringComparer.Ordinal, cancellationToken)
                    .ConfigureAwait(false);

            var (lines, lineError) = OrderLineFactory.Build(businessId, id, request.Lines, profilesById, request.Lenient);
            if (lines is null)
            {
                return OrderResult.Fail(lineError);
            }

            var entity = Order.Create(
                id,
                businessId,
                request.CustomerId,
                request.DueDate,
                request.FittingDate,
                request.AssignedToUserId,
                request.Notes,
                lines,
                now);

            if (OrderLineFactory.TotalMismatch(request.TotalAmount, entity.TotalAmount))
            {
                return OrderResult.Fail(OrderError.TotalMismatch);
            }

            await EnsureUniqueDisplayRefAsync(entity, cancellationToken).ConfigureAwait(false);
            _db.Orders.Add(entity);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (DbUpdateException) when (attempt < 3)
                {
                    var raced = await _db.Orders
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
                        .ConfigureAwait(false);
                    if (raced is not null)
                    {
                        _db.Orders.Entry(entity).State = EntityState.Detached;
                        return string.Equals(raced.BusinessId, businessId, StringComparison.Ordinal)
                            ? OrderResult.Ok(await GetByIdAsync(id, cancellationToken).ConfigureAwait(false))
                            : OrderResult.Fail(OrderError.IdConflict);
                    }

                    // Otherwise the (BusinessId, DisplayRef) unique index raced: pick a new ref and retry.
                    entity.ReassignDisplayRef(Order.NewRandomDisplayRef());
                }
            }

            return OrderResult.Ok(await GetByIdAsync(entity.Id, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException ex)
        {
            return OrderResult.Fail(OrderLineFactory.FromException(ex), ex.Message);
        }
    }

    public async Task<OrderResult> UpdateAsync(
        string id,
        UpdateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        var entity = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return OrderResult.Fail(OrderError.NotFound);
        }

        if (StaleUpdate.IsStale(request.BaseModifiedUtc, entity.DateModifiedUtc))
        {
            return OrderResult.Fail(OrderError.Stale);
        }

        // Re-saving the current assignee is allowed even if they have since been deactivated (editing notes on
        // their order must not fail); a newly chosen assignee must be an active member.
        if (!string.Equals(request.AssignedToUserId?.Trim(), entity.AssignedToUserId, StringComparison.Ordinal) &&
            !await IsActiveMemberAsync(request.AssignedToUserId, cancellationToken).ConfigureAwait(false))
        {
            return OrderResult.Fail(OrderError.AssigneeInvalid);
        }

        try
        {
            entity.UpdateHeader(
                request.DueDate,
                request.FittingDate,
                request.AssignedToUserId,
                request.Notes,
                _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OrderResult.Ok(await GetByIdAsync(entity.Id, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException ex)
        {
            return OrderResult.Fail(OrderLineFactory.FromException(ex), ex.Message);
        }
    }

    public async Task<OrderResult> UpdateStatusAsync(
        string id,
        UpdateOrderStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        var entity = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return OrderResult.Fail(OrderError.NotFound);
        }

        if (StaleUpdate.IsStale(request.BaseModifiedUtc, entity.DateModifiedUtc))
        {
            return OrderResult.Fail(OrderError.Stale);
        }

        try
        {
            entity.SetStatus(request.Status, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return OrderResult.Ok(await GetByIdAsync(entity.Id, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException ex)
        {
            return OrderResult.Fail(OrderLineFactory.FromException(ex), ex.Message);
        }
    }

    public Task<OrderError> DeleteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        // Locked against a payment being recorded at the same moment (else the FK would fail with a 500).
        return _db.HasActiveTransaction
            ? DeleteLockedAsync(id, cancellationToken)
            : _db.InTransactionAsync(async ct =>
            {
                var error = await DeleteLockedAsync(id, ct).ConfigureAwait(false);
                return (error, error == OrderError.None);
            }, cancellationToken);
    }

    private async Task<OrderError> DeleteLockedAsync(string id, CancellationToken cancellationToken)
    {
        await _db.LockOrderAsync(id, cancellationToken).ConfigureAwait(false);
        var entity = await _db.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return OrderError.NotFound;
        }

        if (await _db.Payments.AsNoTracking().AnyAsync(p => p.OrderId == id, cancellationToken).ConfigureAwait(false))
        {
            return OrderError.HasPayments; // money records are never deleted with their order
        }

        _db.Orders.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OrderError.None;
    }

    /// <summary>Null/blank assignee is allowed; otherwise must be an active member of this business.</summary>
    private async Task<bool> IsActiveMemberAsync(string? userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return true;
        }

        var trimmed = userId.Trim();
        return await _db.StaffMemberships.AsNoTracking()
            .AnyAsync(m => m.UserId == trimmed && m.IsActive, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnsureUniqueDisplayRefAsync(Order entity, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 5; i++)
        {
            var taken = await _db.Orders.AsNoTracking()
                .AnyAsync(o => o.DisplayRef == entity.DisplayRef, cancellationToken)
                .ConfigureAwait(false);
            if (!taken)
            {
                return;
            }

            entity.ReassignDisplayRef(Order.NewRandomDisplayRef());
        }
    }

    private static OrderDetailDto MapDetail(
        Order order,
        string customerName,
        string customerPhone,
        IReadOnlyList<OrderLine> lines,
        DateOnly today) =>
        new(
            order.Id,
            order.BusinessId,
            order.DisplayRef,
            order.CustomerId,
            customerName,
            customerPhone,
            order.Status,
            order.DueDate,
            order.FittingDate,
            order.AssignedToUserId,
            order.TotalAmount,
            order.Notes,
            order.CreatedAtUtc,
            order.DateModifiedUtc,
            OrderStatus.IsOverdue(order.Status, order.DueDate, today),
            lines.Select(l => new OrderLineDto(
                l.Id,
                l.GarmentTemplate,
                l.Quantity,
                l.UnitPrice,
                l.LineTotal,
                l.MeasurementProfileId,
                l.Unit,
                MeasurementFieldsSerializer.Deserialize(l.MeasurementFieldsJson),
                l.Notes,
                l.SortOrder)).ToList());

    private string EnsureBusiness()
    {
        if (string.IsNullOrEmpty(_currentUser.BusinessId))
        {
            throw new InvalidOperationException("Business context is required.");
        }

        return _currentUser.BusinessId;
    }
}
