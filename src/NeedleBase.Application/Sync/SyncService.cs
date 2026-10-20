using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Application.Orders;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Sync;

/// <summary>
/// Offline sync use cases (Slice 4). Push applies device operations through the same use-case
/// services as the REST endpoints (same validation and tenancy), adding optimistic concurrency:
/// an update/delete whose <c>BaseModifiedUtc</c> no longer matches the server is a conflict and
/// nothing is overwritten. Pull streams changes by keyset cursor in dependency order.
/// </summary>
public sealed class SyncService : ISyncService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly ICustomerService _customers;
    private readonly IMeasurementService _measurements;
    private readonly IOrderService _orders;

    public SyncService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        ICustomerService customers,
        IMeasurementService measurements,
        IOrderService orders)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _customers = customers;
        _measurements = measurements;
        _orders = orders;
    }

    // ---------------------------------------------------------------- push

    public async Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        var results = new List<SyncPushResult>(request.Operations.Count);
        foreach (var op in request.Operations)
        {
            SyncPushResult result;
            try
            {
                // One transaction per operation: an order create + status change lands together or not at all,
                // and a rejected op leaves nothing behind.
                result = await _db.InTransactionAsync(async ct =>
                {
                    var r = await ApplyAsync(op, ct).ConfigureAwait(false);
                    return (r, r.Status == SyncOpStatus.Applied);
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                result = Failed(op, SyncErrorCodes.PayloadInvalid, ex.Message);
            }
            catch (DbUpdateException)
            {
                result = Rejected(op);
            }

            if (result.Status != SyncOpStatus.Applied)
            {
                // Never let a rejected op's tracked changes leak into the next op's SaveChanges.
                _db.ChangeTracker.Clear();
            }

            results.Add(result);
        }

        return new SyncPushResponse(results);
    }

    private Task<SyncPushResult> ApplyAsync(SyncPushOperation op, CancellationToken ct)
    {
        if (op.Operation is not (SyncOperation.Upsert or SyncOperation.Delete))
        {
            return Task.FromResult(Failed(op, SyncErrorCodes.PayloadInvalid, "Unknown operation."));
        }

        return op.EntityType switch
        {
            SyncEntityType.Customer => ApplyCustomerAsync(op, ct),
            SyncEntityType.Measurement => ApplyMeasurementAsync(op, ct),
            SyncEntityType.Order => ApplyOrderAsync(op, ct),
            _ => Task.FromResult(Failed(op, SyncErrorCodes.PayloadInvalid, "Unknown entity type."))
        };
    }

    private async Task<SyncPushResult> ApplyCustomerAsync(SyncPushOperation op, CancellationToken ct)
    {
        var existing = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == op.EntityId, ct).ConfigureAwait(false);

        if (op.Operation == SyncOperation.Delete)
        {
            if (existing is null)
            {
                return Applied(op);
            }

            if (IsStale(op, existing.CreatedAtUtc, existing.DateModifiedUtc))
            {
                return Conflict(op, customer: SyncMapper.ToDto(existing));
            }

            return await _customers.DeleteAsync(op.EntityId, ct).ConfigureAwait(false) switch
            {
                // Not a dead end: the device restores the customer (and its orders) from the server copy.
                CustomerDeleteResult.HasOrders => Conflict(op, customer: SyncMapper.ToDto(existing), code: SyncErrorCodes.DeleteRefused),
                _ => Applied(op)
            };
        }

        var dto = op.Customer;
        if (dto is null || dto.Id != op.EntityId)
        {
            return Failed(op, SyncErrorCodes.PayloadInvalid, "Missing customer.");
        }

        if (existing is null)
        {
            if (op.BaseModifiedUtc is not null)
            {
                return Conflict(op, code: SyncErrorCodes.Deleted);
            }

            var (created, error) = await _customers.CreateAsync(
                new CreateCustomerRequest(dto.Name, dto.Phone, dto.Notes, dto.PhotoRef, dto.Id), ct).ConfigureAwait(false);
            return created is null
                ? error == "Customer id conflict." ? Rejected(op) : Failed(op, SyncErrorCodes.PayloadInvalid, error)
                : Applied(op, customer: await LoadCustomerAsync(dto.Id, ct).ConfigureAwait(false));
        }

        if (SameCustomer(existing, dto))
        {
            // Replay (lost ack) or no-op edit: nothing to write.
            return Applied(op, customer: SyncMapper.ToDto(existing));
        }

        if (IsStale(op, existing.CreatedAtUtc, existing.DateModifiedUtc))
        {
            return Conflict(op, customer: SyncMapper.ToDto(existing));
        }

        var (updated, updateError) = await _customers.UpdateAsync(
            dto.Id, new UpdateCustomerRequest(dto.Name, dto.Phone, dto.Notes, dto.PhotoRef), ct).ConfigureAwait(false);
        return updated is null
            ? Failed(op, SyncErrorCodes.PayloadInvalid, updateError)
            : Applied(op, customer: await LoadCustomerAsync(dto.Id, ct).ConfigureAwait(false));
    }

    private async Task<SyncPushResult> ApplyMeasurementAsync(SyncPushOperation op, CancellationToken ct)
    {
        var existing = await _db.MeasurementProfiles.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == op.EntityId, ct).ConfigureAwait(false);

        if (op.Operation == SyncOperation.Delete)
        {
            if (existing is null)
            {
                return Applied(op);
            }

            if (IsStale(op, null, existing.DateModifiedUtc))
            {
                return Conflict(op, measurement: SyncMapper.ToDto(existing));
            }

            await _measurements.DeleteAsync(op.EntityId, ct).ConfigureAwait(false);
            return Applied(op);
        }

        var dto = op.Measurement;
        if (dto is null || dto.Id != op.EntityId)
        {
            return Failed(op, SyncErrorCodes.PayloadInvalid, "Missing measurement.");
        }

        var fields = new Dictionary<string, decimal?>(MeasurementFieldsSerializer.Deserialize(dto.FieldsJson));
        if (existing is null)
        {
            if (op.BaseModifiedUtc is not null)
            {
                return Conflict(op, code: SyncErrorCodes.Deleted);
            }

            var (created, error) = await _measurements.CreateAsync(
                dto.CustomerId,
                new CreateMeasurementRequest
                {
                    Id = dto.Id,
                    GarmentTemplate = dto.GarmentTemplate,
                    Unit = dto.Unit,
                    Fields = fields,
                    Notes = dto.Notes,
                    MeasuredAtUtc = dto.MeasuredAtUtc
                },
                ct).ConfigureAwait(false);
            if (created is null)
            {
                return error switch
                {
                    "Customer not found." => Conflict(op, code: SyncErrorCodes.ParentDeleted),
                    "Measurement id conflict." => Rejected(op),
                    _ => Failed(op, SyncErrorCodes.PayloadInvalid, error)
                };
            }

            return Applied(op, measurement: await LoadMeasurementAsync(dto.Id, ct).ConfigureAwait(false));
        }

        if (SameMeasurement(existing, dto))
        {
            return Applied(op, measurement: SyncMapper.ToDto(existing));
        }

        if (IsStale(op, null, existing.DateModifiedUtc))
        {
            return Conflict(op, measurement: SyncMapper.ToDto(existing));
        }

        var (updated, updateError) = await _measurements.UpdateAsync(
            dto.Id,
            new UpdateMeasurementRequest
            {
                GarmentTemplate = dto.GarmentTemplate,
                Unit = dto.Unit,
                Fields = fields,
                Notes = dto.Notes,
                MeasuredAtUtc = dto.MeasuredAtUtc
            },
            ct).ConfigureAwait(false);
        return updated is null
            ? Failed(op, SyncErrorCodes.PayloadInvalid, updateError)
            : Applied(op, measurement: await LoadMeasurementAsync(dto.Id, ct).ConfigureAwait(false));
    }

    private async Task<SyncPushResult> ApplyOrderAsync(SyncPushOperation op, CancellationToken ct)
    {
        var existing = await _db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == op.EntityId, ct).ConfigureAwait(false);

        if (op.Operation == SyncOperation.Delete)
        {
            if (existing is null)
            {
                return Applied(op);
            }

            if (IsStale(op, existing.CreatedAtUtc, existing.DateModifiedUtc))
            {
                return Conflict(op, order: await LoadOrderAsync(op.EntityId, ct).ConfigureAwait(false));
            }

            var deleteError = await _orders.DeleteAsync(op.EntityId, ct).ConfigureAwait(false);
            return deleteError is OrderError.None or OrderError.NotFound
                ? Applied(op)
                : Failed(op, OrderCode(deleteError), OrderErrorMessages.For(OrderCode(deleteError)));
        }

        var dto = op.Order;
        if (dto is null || dto.Id != op.EntityId)
        {
            return Failed(op, SyncErrorCodes.PayloadInvalid, "Missing order.");
        }

        if (existing is null)
        {
            if (op.BaseModifiedUtc is not null)
            {
                return Conflict(op, code: SyncErrorCodes.Deleted);
            }

            var create = await _orders.CreateAsync(
                new CreateOrderRequest
                {
                    Id = dto.Id,
                    CustomerId = dto.CustomerId,
                    DueDate = dto.DueDate,
                    FittingDate = dto.FittingDate,
                    AssignedToUserId = dto.AssignedToUserId,
                    Notes = dto.Notes,
                    TotalAmount = dto.TotalAmount,
                    Lines = dto.Lines.OrderBy(l => l.SortOrder).Select(l => new CreateOrderLineRequest
                    {
                        GarmentTemplate = l.GarmentTemplate,
                        Quantity = l.Quantity,
                        UnitPrice = l.UnitPrice,
                        MeasurementProfileId = l.MeasurementProfileId,
                        Unit = l.Unit,
                        MeasurementFields = new Dictionary<string, decimal?>(MeasurementFieldsSerializer.Deserialize(l.MeasurementFieldsJson)),
                        Notes = l.Notes
                    }).ToList()
                },
                ct).ConfigureAwait(false);
            if (!create.IsSuccess)
            {
                return create.Error switch
                {
                    OrderError.CustomerNotFound => Conflict(op, code: SyncErrorCodes.ParentDeleted),
                    OrderError.IdConflict => Rejected(op),
                    _ => Failed(op, OrderCode(create.Error), OrderErrorMessages.For(OrderCode(create.Error)))
                };
            }

            // Status moved on the device before the first sync (coalesced into the create).
            if (!string.Equals(dto.Status, OrderStatus.New, StringComparison.Ordinal))
            {
                var status = await _orders.UpdateStatusAsync(dto.Id, new UpdateOrderStatusRequest { Status = dto.Status }, ct)
                    .ConfigureAwait(false);
                if (!status.IsSuccess)
                {
                    return Failed(op, OrderCode(status.Error), OrderErrorMessages.For(OrderCode(status.Error)));
                }
            }

            return Applied(op, order: await LoadOrderAsync(dto.Id, ct).ConfigureAwait(false));
        }

        var headerChanged = existing.DueDate != dto.DueDate ||
            existing.FittingDate != dto.FittingDate ||
            !string.Equals(existing.AssignedToUserId, dto.AssignedToUserId, StringComparison.Ordinal) ||
            !string.Equals(existing.Notes, string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(), StringComparison.Ordinal);
        var statusChanged = !string.Equals(existing.Status, dto.Status, StringComparison.Ordinal);
        if (!headerChanged && !statusChanged)
        {
            return Applied(op, order: await LoadOrderAsync(dto.Id, ct).ConfigureAwait(false));
        }

        if (IsStale(op, existing.CreatedAtUtc, existing.DateModifiedUtc))
        {
            return Conflict(op, order: await LoadOrderAsync(dto.Id, ct).ConfigureAwait(false));
        }

        if (headerChanged)
        {
            var header = await _orders.UpdateAsync(
                dto.Id,
                new UpdateOrderRequest
                {
                    DueDate = dto.DueDate,
                    FittingDate = dto.FittingDate,
                    AssignedToUserId = dto.AssignedToUserId,
                    Notes = dto.Notes
                },
                ct).ConfigureAwait(false);
            if (!header.IsSuccess)
            {
                return Failed(op, OrderCode(header.Error), OrderErrorMessages.For(OrderCode(header.Error)));
            }
        }

        if (statusChanged)
        {
            var status = await _orders.UpdateStatusAsync(dto.Id, new UpdateOrderStatusRequest { Status = dto.Status }, ct)
                .ConfigureAwait(false);
            if (!status.IsSuccess)
            {
                return Failed(op, OrderCode(status.Error), OrderErrorMessages.For(OrderCode(status.Error)));
            }
        }

        return Applied(op, order: await LoadOrderAsync(dto.Id, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Optimistic concurrency. A create (null base) for a row that already exists is only a safe
    /// replay if the row was never changed after it was created (lost ack); otherwise it's a conflict.
    /// </summary>
    private static bool IsStale(SyncPushOperation op, DateTimeOffset? createdAt, DateTimeOffset serverModified)
    {
        if (op.BaseModifiedUtc is { } baseModified)
        {
            return !SyncTime.SameInstant(baseModified, serverModified);
        }

        return createdAt is null || !SyncTime.SameInstant(createdAt.Value, serverModified);
    }

    private static bool SameCustomer(Customer c, SyncCustomerDto d) =>
        c.Name == d.Name.Trim() && c.Phone == d.Phone.Trim() &&
        c.Notes == Clean(d.Notes) && c.PhotoRef == Clean(d.PhotoRef);

    private static bool SameMeasurement(MeasurementProfile m, SyncMeasurementDto d) =>
        m.CustomerId == d.CustomerId &&
        m.GarmentTemplate == d.GarmentTemplate.Trim().ToLowerInvariant() &&
        m.Unit == d.Unit.Trim().ToLowerInvariant() &&
        m.Notes == Clean(d.Notes) &&
        SyncTime.SameInstant(m.MeasuredAtUtc, d.MeasuredAtUtc) &&
        MeasurementFieldsSerializer.Serialize(MeasurementFieldsSerializer.Deserialize(m.FieldsJson)) ==
        MeasurementFieldsSerializer.Serialize(MeasurementFieldsSerializer.Deserialize(d.FieldsJson));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string OrderCode(OrderError error) => error switch
    {
        OrderError.NotFound => OrderErrorMessages.OrderNotFound,
        OrderError.CustomerNotFound => OrderErrorMessages.CustomerNotFound,
        OrderError.MeasurementNotFound => OrderErrorMessages.MeasurementNotFound,
        OrderError.IdConflict => OrderErrorMessages.OrderConflict,
        OrderError.Locked => OrderErrorMessages.OrderLocked,
        OrderError.StatusInvalid => OrderErrorMessages.OrderStatusInvalid,
        OrderError.AssigneeInvalid => OrderErrorMessages.AssigneeInvalid,
        OrderError.TotalMismatch => OrderErrorMessages.TotalMismatch,
        _ => OrderErrorMessages.Validation
    };

    private async Task<SyncCustomerDto?> LoadCustomerAsync(string id, CancellationToken ct)
    {
        var entity = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        return entity is null ? null : SyncMapper.ToDto(entity);
    }

    private async Task<SyncMeasurementDto?> LoadMeasurementAsync(string id, CancellationToken ct)
    {
        var entity = await _db.MeasurementProfiles.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct).ConfigureAwait(false);
        return entity is null ? null : SyncMapper.ToDto(entity);
    }

    private async Task<SyncOrderDto?> LoadOrderAsync(string id, CancellationToken ct)
    {
        var entity = await _db.Orders.AsNoTracking().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, ct).ConfigureAwait(false);
        return entity is null ? null : SyncMapper.ToDto(entity, entity.Lines);
    }

    private static SyncPushResult Applied(
        SyncPushOperation op,
        SyncCustomerDto? customer = null,
        SyncMeasurementDto? measurement = null,
        SyncOrderDto? order = null) =>
        new(op.OpId, SyncOpStatus.Applied, null, null, customer, measurement, order);

    private static SyncPushResult Conflict(
        SyncPushOperation op,
        SyncCustomerDto? customer = null,
        SyncMeasurementDto? measurement = null,
        SyncOrderDto? order = null,
        string code = SyncErrorCodes.StaleBase) =>
        new(op.OpId, SyncOpStatus.Conflict, code,
            code switch
            {
                SyncErrorCodes.Deleted => "This record was deleted on another device.",
                SyncErrorCodes.ParentDeleted => "Its customer was deleted on another device.",
                SyncErrorCodes.DeleteRefused => "This customer has orders, so they weren't deleted.",
                _ => "This record was changed on another device."
            },
            customer, measurement, order);

    private static SyncPushResult Rejected(SyncPushOperation op) =>
        new(op.OpId, SyncOpStatus.Failed, SyncErrorCodes.Rejected, "This record could not be saved.");

    private static SyncPushResult Failed(SyncPushOperation op, string code, string? message) =>
        new(op.OpId, SyncOpStatus.Failed, code, message ?? "This change could not be saved.");

    // ---------------------------------------------------------------- pull

    public async Task<(SyncPullResponse? Page, string? ErrorCode)> PullAsync(SyncPullRequest request, CancellationToken cancellationToken = default)
    {
        EnsureBusiness();
        var now = SyncTime.Normalize(_clock.UtcNow);
        if (!SyncCursor.TryDecode(request.Cursor, out var cursor) ||
            (!cursor.IsFresh && now.UtcTicks - cursor.IssuedTicks > SyncTime.TombstoneRetention.Ticks))
        {
            // Unreadable, old-format, or older than tombstone retention (deletes may be gone): resync from scratch.
            return (null, SyncErrorCodes.CursorInvalid);
        }

        var fresh = cursor.IsFresh;
        long epoch;
        if (cursor.NeedsWatermark)
        {
            // A same-epoch cursor that has read past today's cap proves the ids went backwards (restore that kept
            // SyncState). Old-epoch cursors are just rejected below; they must not bump again (no bump storm).
            var minimumCap = !fresh && cursor.Epoch == await _db.GetSyncEpochAsync(cancellationToken).ConfigureAwait(false)
                ? cursor.HighestPosition
                : 0;
            var watermark = await _db.GetSyncWatermarkAsync(minimumCap, cancellationToken).ConfigureAwait(false);
            cursor.Cap = watermark.Cap;
            epoch = watermark.Epoch;
        }
        else
        {
            epoch = await _db.GetSyncEpochAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!fresh && cursor.Epoch != epoch)
        {
            // Positions from another cluster's transaction ids mean nothing here.
            return (null, SyncErrorCodes.EpochChanged);
        }

        cursor.Epoch = epoch;

        if (fresh)
        {
            // A first pull sends current rows only: every tombstone below the cap is already reflected.
            cursor.Deletes = new SyncPosition(cursor.Cap, string.Empty);
        }

        var cap = cursor.Cap;
        var limit = Math.Clamp(request.Limit, 1, Domain.Common.FieldLimits.SyncPullLimitMax);
        var remaining = limit;

        var customers = new List<SyncCustomerDto>();
        var measurements = new List<SyncMeasurementDto>();
        var orders = new List<SyncOrderDto>();
        var deleted = new List<SyncDeletedItemDto>();
        var hasMore = false;

        while (cursor.Phase <= SyncCursor.PhaseDeletes)
        {
            int fetched;
            switch (cursor.Phase)
            {
                case SyncCursor.PhaseCustomers:
                {
                    var rows = await After(_db.Customers.AsNoTracking(), cursor.Customers, cap)
                        .Take(remaining)
                        .Select(c => new { Entity = c, Tx = EF.Property<long>(c, SyncTime.TxColumn) })
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    customers.AddRange(rows.Select(r => SyncMapper.ToDto(r.Entity)));
                    if (rows.Count > 0)
                    {
                        cursor.Customers = new SyncPosition(rows[^1].Tx, rows[^1].Entity.Id);
                    }

                    fetched = rows.Count;
                    break;
                }

                case SyncCursor.PhaseMeasurements:
                {
                    var rows = await After(_db.MeasurementProfiles.AsNoTracking(), cursor.Measurements, cap)
                        .Take(remaining)
                        .Select(m => new { Entity = m, Tx = EF.Property<long>(m, SyncTime.TxColumn) })
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    measurements.AddRange(rows.Select(r => SyncMapper.ToDto(r.Entity)));
                    if (rows.Count > 0)
                    {
                        cursor.Measurements = new SyncPosition(rows[^1].Tx, rows[^1].Entity.Id);
                    }

                    fetched = rows.Count;
                    break;
                }

                case SyncCursor.PhaseOrders:
                {
                    var rows = await After(_db.Orders.AsNoTracking(), cursor.Orders, cap)
                        .Take(remaining)
                        .Select(o => new
                        {
                            Entity = o,
                            Lines = o.Lines.OrderBy(l => l.SortOrder).ToList(),
                            Tx = EF.Property<long>(o, SyncTime.TxColumn)
                        })
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    orders.AddRange(rows.Select(r => SyncMapper.ToDto(r.Entity, r.Lines)));
                    if (rows.Count > 0)
                    {
                        cursor.Orders = new SyncPosition(rows[^1].Tx, rows[^1].Entity.Id);
                    }

                    fetched = rows.Count;
                    break;
                }

                default:
                {
                    var rows = await After(_db.DeletedItems.AsNoTracking(), cursor.Deletes, cap)
                        .Take(remaining)
                        .Select(d => new { Entity = d, Tx = EF.Property<long>(d, SyncTime.TxColumn) })
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    deleted.AddRange(rows.Select(r =>
                        new SyncDeletedItemDto(r.Entity.EntityType, r.Entity.EntityId, SyncTime.Normalize(r.Entity.DeletedAtUtc))));
                    if (rows.Count > 0)
                    {
                        cursor.Deletes = new SyncPosition(rows[^1].Tx, rows[^1].Entity.Id);
                    }

                    fetched = rows.Count;
                    break;
                }
            }

            remaining -= fetched;
            if (remaining == 0)
            {
                // Page full: the current phase may have more rows.
                hasMore = true;
                break;
            }

            cursor.Phase++;
        }

        // Complete: the next sync continues exactly after what was sent (commit-ordered, so no overlap needed).
        var next = hasMore
            ? cursor
            : new SyncCursor
            {
                Customers = cursor.Customers,
                Measurements = cursor.Measurements,
                Orders = cursor.Orders,
                Deletes = cursor.Deletes,
                Epoch = cursor.Epoch
            };
        next.IssuedTicks = now.UtcTicks;
        if (!hasMore)
        {
            next.Cap = 0; // next sync takes a new watermark
        }

        return (new SyncPullResponse(customers, measurements, orders, deleted, next.Encode(), hasMore, now), null);
    }

    /// <summary>Rows after <paramref name="position"/> and strictly below the watermark, in (tx, id) order.</summary>
    private static IOrderedQueryable<T> After<T>(IQueryable<T> source, SyncPosition? position, long cap)
        where T : Entity
    {
        var query = source.Where(e => EF.Property<long>(e, SyncTime.TxColumn) < cap);
        if (position is not null)
        {
            var tx = position.Tx;
            var id = position.Id;
            query = query.Where(e =>
                EF.Property<long>(e, SyncTime.TxColumn) > tx ||
                (EF.Property<long>(e, SyncTime.TxColumn) == tx && e.Id.CompareTo(id) > 0));
        }

        return query.OrderBy(e => EF.Property<long>(e, SyncTime.TxColumn)).ThenBy(e => e.Id);
    }

    private string EnsureBusiness()
    {
        var businessId = _currentUser.BusinessId;
        if (string.IsNullOrWhiteSpace(businessId))
        {
            throw new InvalidOperationException("Business context is required.");
        }

        return businessId;
    }
}
