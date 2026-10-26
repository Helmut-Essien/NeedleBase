using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Orders;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;

namespace NeedleBase.Mobile.Data.Services;

public sealed class OrderLocalService : IOrderLocalService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public OrderLocalService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<IReadOnlyList<OrderListItemDto>> SearchAsync(
        string businessId,
        string? search,
        string? status,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        take = Math.Clamp(take, 1, 100);
        var today = BusinessTime.Today(DateTimeOffset.UtcNow);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        // Filter on entities (translatable), order by ULID Id (time-sortable), Take in SQL.
        var orders = db.Orders.AsNoTracking().Where(o => o.BusinessId == businessId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusFilter = status.Trim();
            if (statusFilter == OrderStatus.ActiveFilter)
            {
                var active = OrderStatus.Active.ToArray();
                orders = orders.Where(o => active.Contains(o.Status));
            }
            else
            {
                orders = orders.Where(o => o.Status == statusFilter);
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Customer part uses the same rule as the Customers tab and New order picker.
            var refTerm = SearchText.Ref(search);
            var customerIds = CustomerSearchFilter
                .Apply(db.Customers.AsNoTracking().Where(c => c.BusinessId == businessId), CustomerSearchKeys.Parse(search))
                .Select(c => c.Id);
            orders = orders.Where(o => o.DisplayRef.Contains(refTerm) || customerIds.Contains(o.CustomerId));
        }

        var rows = await (
            from o in orders
            join c in db.Customers.AsNoTracking() on o.CustomerId equals c.Id
            orderby o.Id descending
            select new
            {
                o.Id,
                o.DisplayRef,
                o.CustomerId,
                CustomerName = c.Name,
                o.Status,
                o.DueDate,
                o.TotalAmount,
                o.DateModifiedUtc
            })
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var syncStates = await LocalSyncStates.ForAsync(db, businessId, SyncEntityType.Order, rows.Select(r => r.Id).ToList(), cancellationToken)
            .ConfigureAwait(false);
        return rows
            .Select(x => new OrderListItemDto(
                x.Id,
                x.DisplayRef,
                x.CustomerId,
                x.CustomerName,
                x.Status,
                x.DueDate,
                x.TotalAmount,
                x.DateModifiedUtc,
                OrderStatus.IsOverdue(x.Status, x.DueDate, today),
                null,
                syncStates.GetValueOrDefault(x.Id)))
            .ToList();
    }

    public async Task<OrderDetailDto?> GetByIdAsync(
        string businessId,
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        var today = BusinessTime.Today(DateTimeOffset.UtcNow);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var order = await db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.BusinessId == businessId && o.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (order is null)
        {
            return null;
        }

        var customer = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Id == order.CustomerId, cancellationToken)
            .ConfigureAwait(false);
        if (customer is null)
        {
            return null;
        }

        var lines = await db.OrderLines.AsNoTracking()
            .Where(l => l.BusinessId == businessId && l.OrderId == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        lines = lines.OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToList();
        return MapDetail(order, customer.Name, customer.Phone, lines, today);
    }

    public async Task<IReadOnlyList<OrderListItemDto>> ListAttentionAsync(
        string businessId,
        int take = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        take = Math.Clamp(take, 1, 20);
        var today = BusinessTime.Today(DateTimeOffset.UtcNow);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var rows = await (
            from o in db.Orders.AsNoTracking()
            join c in db.Customers.AsNoTracking() on o.CustomerId equals c.Id
            where o.BusinessId == businessId
                  && (o.Status == OrderStatus.Ready
                      || (o.DueDate != null
                          && o.DueDate < today
                          && o.Status != OrderStatus.Collected
                          && o.Status != OrderStatus.Cancelled))
            orderby o.Id descending
            select new
            {
                o.Id,
                o.DisplayRef,
                o.CustomerId,
                CustomerName = c.Name,
                o.Status,
                o.DueDate,
                o.TotalAmount,
                o.DateModifiedUtc
            })
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(x => new OrderListItemDto(
                x.Id,
                x.DisplayRef,
                x.CustomerId,
                x.CustomerName,
                x.Status,
                x.DueDate,
                x.TotalAmount,
                x.DateModifiedUtc,
                OrderStatus.IsOverdue(x.Status, x.DueDate, today)))
            .ToList();
    }

    public async Task<OrderCountsDto> GetCountsAsync(
        string businessId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        // One grouped COUNT in SQL (at most 7 rows), not every order row.
        var grouped = await db.Orders.AsNoTracking()
            .Where(o => o.BusinessId == businessId)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byStatus = OrderStatus.All.ToDictionary(s => s, _ => 0);
        foreach (var row in grouped)
        {
            if (byStatus.ContainsKey(row.Status))
            {
                byStatus[row.Status] = row.Count;
            }
        }

        var active = byStatus
            .Where(kv => OrderStatus.Active.Contains(kv.Key))
            .Sum(kv => kv.Value);
        var ready = byStatus[OrderStatus.Ready];

        return new OrderCountsDto(active, ready, byStatus);
    }

    public async Task<RecordResult<OrderDetailDto>> SaveAsync(
        string businessId,
        CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        var now = DateTimeOffset.UtcNow;
        var id = string.IsNullOrWhiteSpace(request.Id) ? IdGenerator.NewId() : request.Id.Trim();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var existing = await db.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.BusinessId == businessId && o.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            var replay = await GetByIdAsync(businessId, id, cancellationToken).ConfigureAwait(false);
            return replay is null ? RecordResult.Fail(RecordError.Validation, OrderErrorMessages.Fallback) : replay;
        }

        var customerExists = await db.Customers.AsNoTracking()
            .AnyAsync(c => c.BusinessId == businessId && c.Id == request.CustomerId, cancellationToken)
            .ConfigureAwait(false);
        if (!customerExists)
        {
            return Fail(OrderError.CustomerNotFound, null);
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            return Fail(OrderError.Invalid, "no lines");
        }

        try
        {
            var profileIds = OrderLineFactory.ProfileIds(request.Lines);
            var profilesById = profileIds.Count == 0
                ? new Dictionary<string, MeasurementProfile>(StringComparer.Ordinal)
                : await db.MeasurementProfiles.AsNoTracking()
                    .Where(m =>
                        m.BusinessId == businessId &&
                        m.CustomerId == request.CustomerId &&
                        profileIds.Contains(m.Id))
                    .ToDictionaryAsync(m => m.Id, StringComparer.Ordinal, cancellationToken)
                    .ConfigureAwait(false);

            var (lines, lineError) = OrderLineFactory.Build(businessId, id, request.Lines, profilesById);
            if (lines is null)
            {
                return Fail(lineError, null);
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
                return Fail(OrderError.TotalMismatch, null);
            }

            // (BusinessId, DisplayRef) is unique; single writer on device, so a pre-check suffices.
            for (var i = 0; i < 5 && await db.Orders.AsNoTracking()
                     .AnyAsync(o => o.BusinessId == businessId && o.DisplayRef == entity.DisplayRef, cancellationToken)
                     .ConfigureAwait(false); i++)
            {
                entity.ReassignDisplayRef(Order.NewRandomDisplayRef());
            }

            db.Orders.Add(entity);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return await GetByIdAsync(businessId, entity.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (LocalStorageErrors.IsStorageFull(ex))
        {
            return RecordResult.Fail(RecordError.StorageFull, LocalStorageErrors.StorageFullMessage);
        }
        catch (ArgumentException ex)
        {
            return Fail(OrderLineFactory.FromException(ex), ex.Message);
        }
        catch (DbUpdateException ex)
        {
            return Fail(OrderError.Invalid, ex.InnerException?.Message ?? ex.Message);
        }
    }

    public async Task<RecordResult<OrderDetailDto>> UpdateStatusAsync(
        string businessId,
        string id,
        string status,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var entity = await db.Orders
            .FirstOrDefaultAsync(o => o.BusinessId == businessId && o.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return Fail(OrderError.NotFound, null);
        }

        try
        {
            entity.SetStatus(status, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var replay = await GetByIdAsync(businessId, id, cancellationToken).ConfigureAwait(false);
            return replay is null ? RecordResult.Fail(RecordError.Validation, OrderErrorMessages.Fallback) : replay;
        }
        catch (Exception ex) when (LocalStorageErrors.IsStorageFull(ex))
        {
            return RecordResult.Fail(RecordError.StorageFull, LocalStorageErrors.StorageFullMessage);
        }
        catch (ArgumentException ex)
        {
            return Fail(OrderLineFactory.FromException(ex), ex.Message);
        }
    }

    /// <summary>
    /// Owner/Manager edit (due date, notes, lines incl. price) with the server's rules (<see cref="OrderEditor"/>):
    /// role, collected/cancelled lock, total not below the locally known amount paid. Saved offline; the auto-queue
    /// sends the order with its lines and the base version, so a newer server edit comes back as a conflict.
    /// </summary>
    public async Task<RecordResult<OrderDetailDto>> UpdateAsync(
        string businessId,
        string id,
        UpdateOrderRequest request,
        string? role,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentNullException.ThrowIfNull(request);
        if (!OrderEditor.CanEdit(role))
        {
            return Fail(OrderError.Forbidden, null);
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();

        var entity = await db.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.BusinessId == businessId && o.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return Fail(OrderError.NotFound, null);
        }

        if (StaleUpdate.IsStale(request.BaseModifiedUtc, entity.DateModifiedUtc))
        {
            return Fail(OrderError.Stale, null);
        }

        try
        {
            // Only what the edit needs (low-end phones): profiles for new lines, paid only when lines change.
            // SQLite stores decimals as text, so the sum runs here over the non-voided amounts only.
            IReadOnlyList<MeasurementProfile> profiles = [];
            decimal paid = 0;
            if (request.Lines is { } reqLines)
            {
                var (ids, garments) = OrderEditor.ProfileNeeds(entity, reqLines);
                if (ids.Count + garments.Count > 0)
                {
                    profiles = await db.MeasurementProfiles.AsNoTracking()
                        .Where(m => m.BusinessId == businessId && m.CustomerId == entity.CustomerId &&
                                    (ids.Contains(m.Id) || garments.Contains(m.GarmentTemplate)))
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                }

                paid = (await db.Payments.AsNoTracking()
                    .Where(p => p.BusinessId == businessId && p.OrderId == id && !p.IsVoided)
                    .Select(p => p.Amount)
                    .ToListAsync(cancellationToken).ConfigureAwait(false)).Sum();
            }

            var (error, detail) = OrderEditor.Apply(entity, request, profiles, paid, DateTimeOffset.UtcNow);
            if (error != OrderError.None)
            {
                return Fail(error, detail);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var replay = await GetByIdAsync(businessId, id, cancellationToken).ConfigureAwait(false);
            return replay is null ? RecordResult.Fail(RecordError.Validation, OrderErrorMessages.Fallback) : replay;
        }
        catch (Exception ex) when (LocalStorageErrors.IsStorageFull(ex))
        {
            return RecordResult.Fail(RecordError.StorageFull, LocalStorageErrors.StorageFullMessage);
        }
        catch (ArgumentException ex)
        {
            return Fail(OrderLineFactory.FromException(ex), ex.Message);
        }
    }

    /// <summary>
    /// Friendly, user-safe copy for the UI. Technical detail goes to the debug log only
    /// (never <c>ex.Message</c> to the screen).
    /// </summary>
    private static RecordFailure Fail(OrderError error, string? technicalDetail)
    {
        if (technicalDetail is not null)
        {
            System.Diagnostics.Debug.WriteLine($"OrderLocalService {error}: {technicalDetail}");
        }

        // Detail = the order copy (same wording as before); never ex.Message.
        return RecordResult.Fail(OrderErrorMapping.ToRecord(error), OrderErrorMessages.For(OrderErrorMapping.Code(error)));
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
                l.SortOrder,
                l.MeasurementTakenAtUtc)).ToList());
}
