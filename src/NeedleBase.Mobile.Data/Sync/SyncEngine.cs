using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;

namespace NeedleBase.Mobile.Data.Sync;

public sealed record SyncQueueSummary(int Pending, int Failed, int Conflicts, DateTimeOffset? LastSyncUtc)
{
    public int NeedsAttention => Failed + Conflicts;
}

public sealed record SyncQueueEntry(
    string Id,
    string EntityType,
    string Label,
    string Action,
    string State,
    string? Problem,
    string? Code,
    bool ServerCopyDeleted);

/// <summary>
/// One sync run = push queued changes (batches of <see cref="BatchSize"/>, parents first), then pull server
/// changes page by page. Each page is applied in one SQLite transaction together with its cursor, with
/// <see cref="LocalDbContext.SuppressSyncQueue"/> set (reset in finally). Callers must not run two at once
/// (the app's SyncService serialises runs).
/// </summary>
public sealed class SyncEngine
{
    public const int BatchSize = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISyncApi _api;

    public SyncEngine(IServiceScopeFactory scopeFactory, ISyncApi api)
    {
        _scopeFactory = scopeFactory;
        _api = api;
    }

    public async Task RunAsync(string businessId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        await PushAsync(businessId, cancellationToken).ConfigureAwait(false);

        if (await IsResyncPendingAsync(businessId, cancellationToken).ConfigureAwait(false))
        {
            await ResyncAsync(businessId, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await PullAsync(businessId, cancellationToken).ConfigureAwait(false);
        }
        catch (SyncCursorInvalidException)
        {
            await ResyncAsync(businessId, cancellationToken).ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------ push

    public async Task PushAsync(string businessId, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            List<SyncQueueItem> batch;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
                // Parents first (a restored customer must exist before its order), then queue order.
                batch = await db.SyncQueue.AsNoTracking()
                    .Where(q => q.BusinessId == businessId && q.State == SyncQueueState.Pending)
                    .OrderBy(q => q.EntityType == SyncEntityType.Customer ? 0 : q.EntityType == SyncEntityType.Measurement ? 1 : 2)
                    .ThenBy(q => q.Id)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
            }

            if (batch.Count == 0)
            {
                return;
            }

            var request = new SyncPushRequest { Operations = batch.Select(ToOperation).ToList() };
            var response = await _api.PushAsync(request, cancellationToken).ConfigureAwait(false);
            await ApplyPushResultsAsync(businessId, batch, response, cancellationToken).ConfigureAwait(false);

            if (batch.Count < BatchSize)
            {
                return;
            }
        }
    }

    private static SyncPushOperation ToOperation(SyncQueueItem item)
    {
        var op = new SyncPushOperation
        {
            OpId = item.Id,
            EntityType = item.EntityType,
            EntityId = item.EntityId,
            Operation = item.Operation,
            BaseModifiedUtc = item.BaseModifiedUtc
        };
        if (item.Operation == SyncOperation.Upsert)
        {
            switch (item.EntityType)
            {
                case SyncEntityType.Customer:
                    op.Customer = SyncJson.Deserialize<SyncCustomerDto>(item.PayloadJson);
                    break;
                case SyncEntityType.Measurement:
                    op.Measurement = SyncJson.Deserialize<SyncMeasurementDto>(item.PayloadJson);
                    break;
                case SyncEntityType.Order:
                    op.Order = SyncJson.Deserialize<SyncOrderDto>(item.PayloadJson);
                    break;
            }
        }

        return op;
    }

    private async Task ApplyPushResultsAsync(
        string businessId,
        IReadOnlyList<SyncQueueItem> sent,
        SyncPushResponse response,
        CancellationToken cancellationToken)
    {
        var results = response.Results.ToDictionary(r => r.OpId, StringComparer.Ordinal);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        db.SuppressSyncQueue = true;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var sentIds = sent.Select(s => s.Id).ToList();
            var items = await db.SyncQueue.Where(q => sentIds.Contains(q.Id)).ToDictionaryAsync(q => q.Id, cancellationToken)
                .ConfigureAwait(false);
            var acked = results.Values.Where(r => r.Status == SyncOpStatus.Applied).ToList();
            var state = await LocalState.LoadAsync(db, businessId,
                acked.Select(r => r.Customer?.Id).Concat(acked.Select(r => r.Measurement?.CustomerId)).Concat(acked.Select(r => r.Order?.CustomerId)),
                acked.Select(r => r.Measurement?.Id),
                acked.Select(r => r.Order?.Id),
                cancellationToken).ConfigureAwait(false);

            foreach (var sentItem in sent)
            {
                if (!results.TryGetValue(sentItem.Id, out var result) || !items.TryGetValue(sentItem.Id, out var item))
                {
                    continue; // not answered: stays pending
                }

                // The user edited the record while the push was in flight: keep their newer change queued.
                var editedSince = item.PayloadJson != sentItem.PayloadJson || item.Operation != sentItem.Operation;

                switch (result.Status)
                {
                    case SyncOpStatus.Applied:
                        if (editedSince)
                        {
                            item.BaseModifiedUtc = ServerModified(result) ?? item.BaseModifiedUtc;
                        }
                        else
                        {
                            db.SyncQueue.Remove(item);
                            ApplyServerCopy(db, state, businessId, result.Customer, result.Measurement, result.Order);
                            if (item.Operation == SyncOperation.Delete && item.EntityType == SyncEntityType.Customer)
                            {
                                // Confirmed gone on the server: children waiting for it never will arrive.
                                await db.SyncDeferred.Where(d => d.BusinessId == businessId && d.ParentId == item.EntityId)
                                    .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                            }
                        }

                        break;

                    case SyncOpStatus.Conflict:
                        item.State = SyncQueueState.Conflict;
                        item.ConflictCode = result.Code;
                        item.LastError = ConflictText(result.Code);
                        item.ConflictServerJson = result.Customer is not null ? SyncJson.Serialize(result.Customer)
                            : result.Measurement is not null ? SyncJson.Serialize(result.Measurement)
                            : result.Order is not null ? SyncJson.Serialize(result.Order)
                            : null;
                        break;

                    default:
                        item.State = SyncQueueState.Failed;
                        item.ConflictCode = result.Code;
                        item.RetryCount++;
                        item.LastError = result.Message ?? "This change could not be saved.";
                        break;
                }
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await ApplyDeferredAsync(db, businessId, cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            db.SuppressSyncQueue = false;
        }
    }

    internal static string ConflictText(string? code) => code switch
    {
        SyncErrorCodes.Deleted => "Deleted on another device.",
        SyncErrorCodes.ParentDeleted => "Its customer was deleted on another device.",
        SyncErrorCodes.DeleteRefused => "This customer has orders, so they weren't deleted.",
        _ => "Changed on another device."
    };

    private static DateTimeOffset? ServerModified(SyncPushResult result) =>
        result.Customer?.DateModifiedUtc ?? result.Measurement?.DateModifiedUtc ?? result.Order?.DateModifiedUtc;

    // ------------------------------------------------------------------ pull

    public Task PullAsync(string businessId, CancellationToken cancellationToken = default) =>
        PullCoreAsync(businessId, seen: null, cancellationToken);

    private async Task PullCoreAsync(string businessId, SeenIds? seen, CancellationToken cancellationToken)
    {
        string? cursor = null;
        if (seen is null)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            cursor = (await db.DeviceInfo.AsNoTracking()
                .FirstOrDefaultAsync(d => d.BusinessId == businessId, cancellationToken).ConfigureAwait(false))?.PullCursor;
        }

        while (true)
        {
            var page = await _api.PullAsync(new SyncPullRequest { Cursor = cursor, Limit = BatchSize }, cancellationToken)
                .ConfigureAwait(false);
            seen?.Add(page);
            var last = !page.HasMore;
            await ApplyPageAsync(businessId, page, last && seen is not null ? seen : null, cancellationToken).ConfigureAwait(false);
            cursor = page.Cursor;
            if (last)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Full pull from scratch (cursor rejected/expired, or a discard/restore needs server copies). Rows this
    /// device has that the server no longer sends are pruned, unless they still have queued changes.
    /// If the app dies mid-way, <see cref="DeviceInfo.ResyncPending"/> makes the next run start over.
    /// </summary>
    public async Task ResyncAsync(string businessId, CancellationToken cancellationToken = default)
    {
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            var info = await GetOrAddDeviceInfoAsync(db, businessId, cancellationToken).ConfigureAwait(false);
            info.ResyncPending = true;
            info.PullCursor = null;
            await db.SyncDeferred.Where(d => d.BusinessId == businessId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await PullCoreAsync(businessId, new SeenIds(), cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyPageAsync(string businessId, SyncPullResponse page, SeenIds? pruneWith, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        db.SuppressSyncQueue = true;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            // Local unsynced changes win until they are pushed or resolved.
            var queue = await db.SyncQueue.Where(q => q.BusinessId == businessId).ToListAsync(cancellationToken).ConfigureAwait(false);
            var queued = queue.ToDictionary(q => q.EntityType + ":" + q.EntityId, StringComparer.Ordinal);

            var deletedCustomerIds = page.Deleted.Where(d => d.EntityType == SyncEntityType.Customer).Select(d => (string?)d.EntityId);
            var state = await LocalState.LoadAsync(db, businessId,
                page.Customers.Select(c => (string?)c.Id)
                    .Concat(page.Measurements.Select(m => (string?)m.CustomerId))
                    .Concat(page.Orders.Select(o => (string?)o.CustomerId))
                    .Concat(deletedCustomerIds),
                page.Measurements.Select(m => (string?)m.Id)
                    .Concat(page.Deleted.Where(d => d.EntityType == SyncEntityType.Measurement).Select(d => (string?)d.EntityId)),
                page.Orders.Select(o => (string?)o.Id)
                    .Concat(page.Deleted.Where(d => d.EntityType == SyncEntityType.Order).Select(d => (string?)d.EntityId)),
                cancellationToken).ConfigureAwait(false);

            foreach (var customer in page.Customers.Where(c => !queued.ContainsKey(SyncEntityType.Customer + ":" + c.Id)))
            {
                ApplyServerCopy(db, state, businessId, customer, null, null);
            }

            var deferredKeys = page.Measurements.Select(m => m.Id).Concat(page.Orders.Select(o => o.Id)).ToList();
            var existingDeferred = await db.SyncDeferred
                .Where(d => d.BusinessId == businessId && deferredKeys.Contains(d.EntityId))
                .ToDictionaryAsync(d => d.EntityType + ":" + d.EntityId, cancellationToken).ConfigureAwait(false);

            foreach (var measurement in page.Measurements.Where(m => !queued.ContainsKey(SyncEntityType.Measurement + ":" + m.Id)))
            {
                if (!state.HasCustomer(measurement.CustomerId))
                {
                    Defer(db, existingDeferred, businessId, SyncEntityType.Measurement, measurement.Id, measurement.CustomerId,
                        SyncJson.Serialize(measurement));
                    continue;
                }

                ApplyServerCopy(db, state, businessId, null, measurement, null);
                Undefer(db, existingDeferred, SyncEntityType.Measurement, measurement.Id);
            }

            foreach (var order in page.Orders.Where(o => !queued.ContainsKey(SyncEntityType.Order + ":" + o.Id)))
            {
                if (!state.HasCustomer(order.CustomerId))
                {
                    Defer(db, existingDeferred, businessId, SyncEntityType.Order, order.Id, order.CustomerId, SyncJson.Serialize(order));
                    continue;
                }

                ApplyServerCopy(db, state, businessId, null, null, order);
                Undefer(db, existingDeferred, SyncEntityType.Order, order.Id);
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (page.Deleted.Count > 0)
            {
                await ApplyDeletesAsync(db, state, businessId, page.Deleted, queued, queue, cancellationToken).ConfigureAwait(false);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            await ApplyDeferredAsync(db, businessId, cancellationToken).ConfigureAwait(false);

            if (pruneWith is not null)
            {
                await PruneAsync(db, businessId, pruneWith, cancellationToken).ConfigureAwait(false);
            }

            // Cursor saved in the same transaction as the page: a kill can't leave them out of step.
            var info = await GetOrAddDeviceInfoAsync(db, businessId, cancellationToken).ConfigureAwait(false);
            info.PullCursor = page.Cursor;
            if (!page.HasMore)
            {
                info.LastSyncUtc = DateTimeOffset.UtcNow;
                if (pruneWith is not null)
                {
                    info.ResyncPending = false;
                }
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            db.SuppressSyncQueue = false;
        }
    }

    private static void Defer(LocalDbContext db, Dictionary<string, SyncDeferredItem> existing, string businessId, string type,
        string id, string parentId, string json)
    {
        if (existing.TryGetValue(type + ":" + id, out var row))
        {
            row.ParentId = parentId;
            row.PayloadJson = json;
            return;
        }

        row = new SyncDeferredItem
        {
            Id = IdGenerator.NewId(),
            BusinessId = businessId,
            EntityType = type,
            EntityId = id,
            ParentId = parentId,
            PayloadJson = json,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        existing[type + ":" + id] = row;
        db.SyncDeferred.Add(row);
    }

    private static void Undefer(LocalDbContext db, Dictionary<string, SyncDeferredItem> existing, string type, string id)
    {
        if (existing.Remove(type + ":" + id, out var row))
        {
            db.SyncDeferred.Remove(row);
        }
    }

    /// <summary>Applies deferred children whose customer is now on this device. Caller sets SuppressSyncQueue.</summary>
    private static async Task ApplyDeferredAsync(LocalDbContext db, string businessId, CancellationToken ct)
    {
        var ready = await db.SyncDeferred
            .Where(d => d.BusinessId == businessId && db.Customers.Any(c => c.BusinessId == businessId && c.Id == d.ParentId))
            .ToListAsync(ct).ConfigureAwait(false);
        if (ready.Count == 0)
        {
            return;
        }

        var measurements = ready.Where(d => d.EntityType == SyncEntityType.Measurement)
            .Select(d => SyncJson.Deserialize<SyncMeasurementDto>(d.PayloadJson)!).ToList();
        var orders = ready.Where(d => d.EntityType == SyncEntityType.Order)
            .Select(d => SyncJson.Deserialize<SyncOrderDto>(d.PayloadJson)!).ToList();
        var queuedIds = await db.SyncQueue.Where(q => q.BusinessId == businessId).Select(q => q.EntityId).ToListAsync(ct).ConfigureAwait(false);
        var state = await LocalState.LoadAsync(db, businessId,
            ready.Select(d => (string?)d.ParentId), measurements.Select(m => (string?)m.Id), orders.Select(o => (string?)o.Id), ct)
            .ConfigureAwait(false);
        foreach (var m in measurements.Where(m => !queuedIds.Contains(m.Id)))
        {
            ApplyServerCopy(db, state, businessId, null, m, null);
        }

        foreach (var o in orders.Where(o => !queuedIds.Contains(o.Id)))
        {
            ApplyServerCopy(db, state, businessId, null, null, o);
        }

        db.SyncDeferred.RemoveRange(ready);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static async Task ApplyDeletesAsync(
        LocalDbContext db,
        LocalState state,
        string businessId,
        IReadOnlyList<SyncDeletedItemDto> deletes,
        Dictionary<string, SyncQueueItem> queued,
        List<SyncQueueItem> queue,
        CancellationToken ct)
    {
        var customerIds = deletes.Where(d => d.EntityType == SyncEntityType.Customer).Select(d => d.EntityId).ToList();
        var customersWithOrders = customerIds.Count == 0
            ? new HashSet<string>()
            : (await db.Orders.Where(o => o.BusinessId == businessId && customerIds.Contains(o.CustomerId))
                .Select(o => o.CustomerId).Distinct().ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var lineIds = deletes.Where(d => d.EntityType == SyncEntityType.OrderLine).Select(d => d.EntityId).ToList();
        var lines = lineIds.Count == 0
            ? new Dictionary<string, OrderLine>()
            : await db.OrderLines.Where(l => l.BusinessId == businessId && lineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct)
                .ConfigureAwait(false);
        var deletedIds = deletes.Select(d => d.EntityId).ToList();
        var deferred = await db.SyncDeferred
            .Where(d => d.BusinessId == businessId && (deletedIds.Contains(d.EntityId) || customerIds.Contains(d.ParentId)))
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var deleted in deletes)
        {
            db.SyncDeferred.RemoveRange(deferred.Where(d => d.EntityId == deleted.EntityId &&
                d.EntityType == deleted.EntityType && db.Entry(d).State != EntityState.Deleted));

            if (queued.TryGetValue(deleted.EntityType + ":" + deleted.EntityId, out var item))
            {
                if (item.Operation == SyncOperation.Delete)
                {
                    db.SyncQueue.Remove(item); // both sides agree
                }
                else
                {
                    // Edited here, deleted elsewhere: ask the user (never drop their edit silently).
                    MarkDeletedElsewhere(item, "Deleted on another device.");
                    continue;
                }
            }

            switch (deleted.EntityType)
            {
                case SyncEntityType.Customer:
                {
                    if (!state.Customers.TryGetValue(deleted.EntityId, out var customer) || IsNewer(customer.DateModifiedUtc, deleted))
                    {
                        break;
                    }

                    var dependents = customersWithOrders.Contains(customer.Id) ||
                        queue.Any(q => q.ParentId == customer.Id && db.Entry(q).State != EntityState.Deleted);
                    if (dependents)
                    {
                        // Orders or unsynced work still hang off it here: the user decides (restore or delete here).
                        var conflict = item is not null && db.Entry(item).State != EntityState.Deleted ? item : null;
                        if (conflict is null)
                        {
                            conflict = NewQueueItem(businessId, SyncEntityType.Customer, customer.Id, null, customer.Name,
                                SyncJson.Serialize(Application.Sync.SyncMapper.ToDto(customer)));
                            db.SyncQueue.Add(conflict);
                            queue.Add(conflict);
                            queued[SyncEntityType.Customer + ":" + customer.Id] = conflict;
                        }

                        conflict.Operation = SyncOperation.Upsert;
                        conflict.PayloadJson ??= SyncJson.Serialize(Application.Sync.SyncMapper.ToDto(customer));
                        conflict.BaseModifiedUtc = null;
                        MarkDeletedElsewhere(conflict, "Deleted on another device. It still has orders or unsynced changes here.");
                        break;
                    }

                    db.Customers.Remove(customer); // measurements cascade locally
                    state.Customers.Remove(customer.Id);
                    db.SyncQueue.RemoveRange(queue.Where(q => q.ParentId == customer.Id && db.Entry(q).State != EntityState.Deleted));
                    db.SyncDeferred.RemoveRange(deferred.Where(d => d.ParentId == customer.Id && db.Entry(d).State != EntityState.Deleted));
                    break;
                }

                case SyncEntityType.Measurement:
                    if (state.Measurements.TryGetValue(deleted.EntityId, out var measurement) && !IsNewer(measurement.DateModifiedUtc, deleted))
                    {
                        db.MeasurementProfiles.Remove(measurement);
                    }

                    break;

                case SyncEntityType.Order:
                    if (state.Orders.TryGetValue(deleted.EntityId, out var order) && !IsNewer(order.DateModifiedUtc, deleted))
                    {
                        db.Orders.Remove(order); // lines cascade locally
                    }

                    break;

                case SyncEntityType.OrderLine:
                    if (lines.TryGetValue(deleted.EntityId, out var line))
                    {
                        db.OrderLines.Remove(line);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// A tombstone older than the local row's server modification time is stale: the record was re-created
    /// with the same id after it was deleted (e.g. "Keep mine"). Never delete it.
    /// </summary>
    private static bool IsNewer(DateTimeOffset localModified, SyncDeletedItemDto deleted) =>
        localModified > deleted.DeletedAtUtc;

    private static void MarkDeletedElsewhere(SyncQueueItem item, string message)
    {
        item.State = SyncQueueState.Conflict;
        item.ConflictCode = SyncErrorCodes.Deleted;
        item.LastError = message;
        item.ConflictServerJson = null;
    }

    private static async Task PruneAsync(LocalDbContext db, string businessId, SeenIds seen, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var queue = await db.SyncQueue.AsNoTracking().Where(q => q.BusinessId == businessId)
            .Select(q => new { q.EntityId, q.ParentId }).ToListAsync(ct).ConfigureAwait(false);
        var keep = queue.Select(q => q.EntityId).Concat(queue.Where(q => q.ParentId != null).Select(q => q.ParentId!))
            .ToHashSet(StringComparer.Ordinal);

        var orders = (await db.Orders.Where(o => o.BusinessId == businessId).Select(o => new { o.Id, o.CustomerId }).ToListAsync(ct)
            .ConfigureAwait(false)).Where(o => !seen.Orders.Contains(o.Id) && !keep.Contains(o.Id)).Select(o => o.Id).ToList();
        var measurementIds = (await db.MeasurementProfiles.Where(m => m.BusinessId == businessId).Select(m => m.Id).ToListAsync(ct)
            .ConfigureAwait(false)).Where(id => !seen.Measurements.Contains(id) && !keep.Contains(id)).ToList();
        var remainingOrderCustomers = (await db.Orders.Where(o => o.BusinessId == businessId && !orders.Contains(o.Id))
            .Select(o => o.CustomerId).ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var customerIds = (await db.Customers.Where(c => c.BusinessId == businessId).Select(c => c.Id).ToListAsync(ct)
            .ConfigureAwait(false)).Where(id => !seen.Customers.Contains(id) && !keep.Contains(id) && !remainingOrderCustomers.Contains(id)).ToList();

        await db.Orders.Where(o => o.BusinessId == businessId && orders.Contains(o.Id)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.MeasurementProfiles.Where(m => m.BusinessId == businessId && measurementIds.Contains(m.Id)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.Customers.Where(c => c.BusinessId == businessId && customerIds.Contains(c.Id)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Upserts the server's copy into SQLite using preloaded <paramref name="state"/>. Caller sets SuppressSyncQueue.</summary>
    private static void ApplyServerCopy(
        LocalDbContext db,
        LocalState state,
        string businessId,
        SyncCustomerDto? customer,
        SyncMeasurementDto? measurement,
        SyncOrderDto? order)
    {
        if (customer is not null)
        {
            if (!state.Customers.TryGetValue(customer.Id, out var entity))
            {
                entity = Customer.Create(customer.Id, businessId, customer.Name, customer.Phone, customer.Notes, customer.PhotoRef,
                    customer.CreatedAtUtc);
                db.Customers.Add(entity);
                state.Customers[entity.Id] = entity;
            }
            else
            {
                entity.Update(customer.Name, customer.Phone, customer.Notes, customer.PhotoRef, customer.DateModifiedUtc);
            }

            var entry = db.Entry(entity);
            entry.Property(c => c.CreatedAtUtc).CurrentValue = customer.CreatedAtUtc;
            entry.Property(c => c.DateModifiedUtc).CurrentValue = customer.DateModifiedUtc;
        }

        if (measurement is not null && state.HasCustomer(measurement.CustomerId))
        {
            if (!state.Measurements.TryGetValue(measurement.Id, out var entity))
            {
                entity = MeasurementProfile.Create(measurement.Id, businessId, measurement.CustomerId, measurement.GarmentTemplate,
                    measurement.Unit, measurement.FieldsJson, measurement.Notes, measurement.MeasuredAtUtc, measurement.DateModifiedUtc);
                db.MeasurementProfiles.Add(entity);
                state.Measurements[entity.Id] = entity;
            }
            else
            {
                entity.Update(measurement.GarmentTemplate, measurement.Unit, measurement.FieldsJson, measurement.Notes,
                    measurement.MeasuredAtUtc, measurement.DateModifiedUtc);
            }

            db.Entry(entity).Property(m => m.DateModifiedUtc).CurrentValue = measurement.DateModifiedUtc;
        }

        if (order is not null && state.HasCustomer(order.CustomerId))
        {
            var lines = order.Lines.Select(l => OrderLine.Create(l.Id, businessId, order.Id, l.GarmentTemplate, l.Quantity, l.UnitPrice,
                l.MeasurementProfileId, l.Unit, l.MeasurementFieldsJson, l.Notes, l.SortOrder)).ToList();
            if (!state.Orders.TryGetValue(order.Id, out var entity))
            {
                entity = Order.Create(order.Id, businessId, order.CustomerId, order.DueDate, order.FittingDate, order.AssignedToUserId,
                    order.Notes, lines, order.CreatedAtUtc);
                db.Orders.Add(entity);
                state.Orders[entity.Id] = entity;
            }
            else
            {
                // Lines are immutable after create; align ids with the server (it generates line ids).
                var localLines = state.LinesFor(order.Id);
                var serverIds = order.Lines.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
                if (localLines.Count != serverIds.Count || localLines.Any(l => !serverIds.Contains(l.Id)))
                {
                    db.OrderLines.RemoveRange(localLines);
                    db.OrderLines.AddRange(lines);
                    state.SetLines(order.Id, lines);
                }
            }

            var entry = db.Entry(entity);
            entry.Property(o => o.CustomerId).CurrentValue = order.CustomerId;
            entry.Property(o => o.DisplayRef).CurrentValue = order.DisplayRef;
            entry.Property(o => o.Status).CurrentValue = order.Status;
            entry.Property(o => o.DueDate).CurrentValue = order.DueDate;
            entry.Property(o => o.FittingDate).CurrentValue = order.FittingDate;
            entry.Property(o => o.AssignedToUserId).CurrentValue = order.AssignedToUserId;
            entry.Property(o => o.TotalAmount).CurrentValue = order.TotalAmount;
            entry.Property(o => o.Notes).CurrentValue = order.Notes;
            entry.Property(o => o.CreatedAtUtc).CurrentValue = order.CreatedAtUtc;
            entry.Property(o => o.DateModifiedUtc).CurrentValue = order.DateModifiedUtc;
        }
    }

    private static async Task<DeviceInfo> GetOrAddDeviceInfoAsync(LocalDbContext db, string businessId, CancellationToken ct)
    {
        var info = db.DeviceInfo.Local.FirstOrDefault(d => d.BusinessId == businessId)
            ?? await db.DeviceInfo.FirstOrDefaultAsync(d => d.BusinessId == businessId, ct).ConfigureAwait(false);
        if (info is null)
        {
            info = new DeviceInfo { BusinessId = businessId, DeviceId = IdGenerator.NewId() };
            db.DeviceInfo.Add(info);
        }

        return info;
    }

    private async Task<bool> IsResyncPendingAsync(string businessId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        return await db.DeviceInfo.AsNoTracking().AnyAsync(d => d.BusinessId == businessId && d.ResyncPending, ct).ConfigureAwait(false);
    }

    private static SyncQueueItem NewQueueItem(string businessId, string type, string id, string? parentId, string label, string? payload) =>
        new()
        {
            Id = IdGenerator.NewId(),
            BusinessId = businessId,
            EntityType = type,
            EntityId = id,
            ParentId = parentId,
            Operation = SyncOperation.Upsert,
            PayloadJson = payload,
            Label = label,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            State = SyncQueueState.Pending
        };

    // ------------------------------------------------------------------ status & resolution

    public async Task<SyncQueueSummary> GetSummaryAsync(string businessId, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var counts = await db.SyncQueue.AsNoTracking()
            .Where(q => q.BusinessId == businessId)
            .GroupBy(q => q.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var lastSync = (await db.DeviceInfo.AsNoTracking()
            .FirstOrDefaultAsync(d => d.BusinessId == businessId, cancellationToken).ConfigureAwait(false))?.LastSyncUtc;
        int Count(string state) => counts.FirstOrDefault(c => c.State == state)?.Count ?? 0;
        return new SyncQueueSummary(Count(SyncQueueState.Pending), Count(SyncQueueState.Failed), Count(SyncQueueState.Conflict), lastSync);
    }

    public async Task<IReadOnlyList<SyncQueueEntry>> ListAsync(string businessId, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var items = await db.SyncQueue.AsNoTracking()
            .Where(q => q.BusinessId == businessId)
            .OrderBy(q => q.Id)
            .Take(500)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return items.Select(q => new SyncQueueEntry(
            q.Id,
            q.EntityType,
            q.Label,
            q.Operation == SyncOperation.Delete ? "Delete" : q.BaseModifiedUtc is null ? "New" : "Update",
            q.State,
            q.LastError,
            q.ConflictCode,
            q.State == SyncQueueState.Conflict && q.ConflictServerJson is null)).ToList();
    }

    /// <summary>Failed items go back to the queue (user tapped Retry).</summary>
    public async Task RetryFailedAsync(string businessId, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        await db.SyncQueue.Where(q => q.BusinessId == businessId && q.State == SyncQueueState.Failed)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.State, SyncQueueState.Pending).SetProperty(q => q.LastError, (string?)null),
                cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Discard a failed change. A never-synced create is removed from this device (with anything that hangs
    /// off it); an edit or delete is dropped and the server's copy is fetched again (resync).
    /// </summary>
    public async Task DiscardAsync(string businessId, string itemId, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var item = await db.SyncQueue.FirstOrDefaultAsync(q => q.BusinessId == businessId && q.Id == itemId, cancellationToken)
            .ConfigureAwait(false);
        if (item is null)
        {
            return;
        }

        db.SuppressSyncQueue = true;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            db.SyncQueue.Remove(item);
            if (item.Operation == SyncOperation.Upsert && item.BaseModifiedUtc is null)
            {
                await DeleteLocalAsync(db, businessId, item.EntityType, item.EntityId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                (await GetOrAddDeviceInfoAsync(db, businessId, cancellationToken).ConfigureAwait(false)).ResyncPending = true;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            db.SuppressSyncQueue = false;
        }

        LocalSyncSignals.RaiseQueueChanged();
    }

    /// <summary>
    /// Conflict choice. Returns false when the choice can't be carried out (e.g. restoring a customer that
    /// is no longer on this device).
    /// <list type="bullet">
    /// <item>Changed elsewhere: Keep mine re-pushes over the server's version; Keep theirs takes it.</item>
    /// <item>Deleted elsewhere: Keep mine re-creates it (and re-queues children waiting on it); Keep theirs deletes it here.</item>
    /// <item>Customer deleted elsewhere (order/measurement): Keep mine restores the customer too; Keep theirs discards this record.</item>
    /// <item>Delete refused (customer has orders): either choice restores the customer from the server.</item>
    /// </list>
    /// </summary>
    public async Task<bool> ResolveConflictAsync(string businessId, string itemId, bool keepMine, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var item = await db.SyncQueue.FirstOrDefaultAsync(
            q => q.BusinessId == businessId && q.Id == itemId && q.State == SyncQueueState.Conflict, cancellationToken)
            .ConfigureAwait(false);
        if (item is null)
        {
            return false;
        }

        db.SuppressSyncQueue = true;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var server = Server(item);
            switch (item.ConflictCode)
            {
                case SyncErrorCodes.DeleteRefused:
                {
                    db.SyncQueue.Remove(item);
                    var state = await LocalState.LoadAsync(db, businessId, [item.EntityId], [], [], cancellationToken).ConfigureAwait(false);
                    ApplyServerCopy(db, state, businessId, server.Customer, null, null);
                    // Its measurements/orders were removed here with it; fetch them again.
                    (await GetOrAddDeviceInfoAsync(db, businessId, cancellationToken).ConfigureAwait(false)).ResyncPending = true;
                    break;
                }

                case SyncErrorCodes.ParentDeleted when keepMine:
                {
                    var parentId = item.ParentId;
                    var parent = parentId is null
                        ? null
                        : await db.Customers.FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Id == parentId, cancellationToken)
                            .ConfigureAwait(false);
                    if (parent is null)
                    {
                        return false;
                    }

                    var parentItem = await db.SyncQueue.FirstOrDefaultAsync(
                        q => q.BusinessId == businessId && q.EntityType == SyncEntityType.Customer && q.EntityId == parent.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (parentItem is null)
                    {
                        db.SyncQueue.Add(NewQueueItem(businessId, SyncEntityType.Customer, parent.Id, null, parent.Name,
                            SyncJson.Serialize(Application.Sync.SyncMapper.ToDto(parent))));
                    }
                    else
                    {
                        Repend(parentItem, baseModified: null);
                        parentItem.Operation = SyncOperation.Upsert;
                        parentItem.PayloadJson = SyncJson.Serialize(Application.Sync.SyncMapper.ToDto(parent));
                    }

                    Repend(item, baseModified: null);
                    await RependChildrenAsync(db, businessId, parent.Id, cancellationToken).ConfigureAwait(false);
                    break;
                }

                case SyncErrorCodes.ParentDeleted:
                    db.SyncQueue.Remove(item);
                    await DeleteLocalAsync(db, businessId, item.EntityType, item.EntityId, cancellationToken).ConfigureAwait(false);
                    break;

                case SyncErrorCodes.Deleted when keepMine:
                case null when keepMine && server.Modified is null:
                    Repend(item, baseModified: null); // re-create; the server drops the stale tombstone
                    if (item.EntityType == SyncEntityType.Customer)
                    {
                        await RependChildrenAsync(db, businessId, item.EntityId, cancellationToken).ConfigureAwait(false);
                    }

                    break;

                case SyncErrorCodes.Deleted:
                case null when server.Modified is null:
                    db.SyncQueue.Remove(item);
                    await DeleteLocalAsync(db, businessId, item.EntityType, item.EntityId, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    if (keepMine)
                    {
                        Repend(item, server.Modified);
                    }
                    else
                    {
                        db.SyncQueue.Remove(item);
                        var state = await LocalState.LoadAsync(db, businessId,
                            [server.Customer?.Id, server.Measurement?.CustomerId, server.Order?.CustomerId],
                            [server.Measurement?.Id], [server.Order?.Id], cancellationToken).ConfigureAwait(false);
                        ApplyServerCopy(db, state, businessId, server.Customer, server.Measurement, server.Order);
                    }

                    break;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            db.SuppressSyncQueue = false;
        }

        LocalSyncSignals.RaiseQueueChanged();
        return true;
    }

    private static void Repend(SyncQueueItem item, DateTimeOffset? baseModified)
    {
        item.State = SyncQueueState.Pending;
        item.LastError = null;
        item.RetryCount = 0;
        item.ConflictCode = null;
        item.ConflictServerJson = null;
        item.BaseModifiedUtc = baseModified;
    }

    /// <summary>Children that were waiting because their customer was deleted elsewhere go back to the queue.</summary>
    private static async Task RependChildrenAsync(LocalDbContext db, string businessId, string customerId, CancellationToken ct)
    {
        var children = await db.SyncQueue.Where(q => q.BusinessId == businessId && q.ParentId == customerId &&
                q.State == SyncQueueState.Conflict && q.ConflictCode == SyncErrorCodes.ParentDeleted)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var child in children)
        {
            Repend(child, baseModified: null);
        }
    }

    /// <summary>Removes a record from this device only (with what hangs off a customer) and its queued changes.</summary>
    private static async Task DeleteLocalAsync(LocalDbContext db, string businessId, string type, string id, CancellationToken ct)
    {
        switch (type)
        {
            case SyncEntityType.Customer:
                await db.Orders.Where(o => o.BusinessId == businessId && o.CustomerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.MeasurementProfiles.Where(m => m.BusinessId == businessId && m.CustomerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.Customers.Where(c => c.BusinessId == businessId && c.Id == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.SyncQueue.Where(q => q.BusinessId == businessId && q.ParentId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.SyncDeferred.Where(d => d.BusinessId == businessId && d.ParentId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                break;
            case SyncEntityType.Measurement:
                await db.MeasurementProfiles.Where(m => m.BusinessId == businessId && m.Id == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                break;
            case SyncEntityType.Order:
                await db.Orders.Where(o => o.BusinessId == businessId && o.Id == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                break;
        }
    }

    private static (DateTimeOffset? Modified, SyncCustomerDto? Customer, SyncMeasurementDto? Measurement, SyncOrderDto? Order) Server(SyncQueueItem item)
    {
        switch (item.EntityType)
        {
            case SyncEntityType.Customer:
                var c = SyncJson.Deserialize<SyncCustomerDto>(item.ConflictServerJson);
                return (c?.DateModifiedUtc, c, null, null);
            case SyncEntityType.Measurement:
                var m = SyncJson.Deserialize<SyncMeasurementDto>(item.ConflictServerJson);
                return (m?.DateModifiedUtc, null, m, null);
            default:
                var o = SyncJson.Deserialize<SyncOrderDto>(item.ConflictServerJson);
                return (o?.DateModifiedUtc, null, null, o);
        }
    }

    /// <summary>Ids the server sent during a resync (everything it still has).</summary>
    private sealed class SeenIds
    {
        public HashSet<string> Customers { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Measurements { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Orders { get; } = new(StringComparer.Ordinal);

        public void Add(SyncPullResponse page)
        {
            Customers.UnionWith(page.Customers.Select(c => c.Id));
            Measurements.UnionWith(page.Measurements.Select(m => m.Id));
            Orders.UnionWith(page.Orders.Select(o => o.Id));
        }
    }

    /// <summary>Tracked local rows for one page/ack, loaded with one query per table (no per-row lookups).</summary>
    private sealed class LocalState
    {
        private readonly Dictionary<string, List<OrderLine>> _lines;

        private LocalState(
            Dictionary<string, Customer> customers,
            Dictionary<string, MeasurementProfile> measurements,
            Dictionary<string, Order> orders,
            Dictionary<string, List<OrderLine>> lines)
        {
            Customers = customers;
            Measurements = measurements;
            Orders = orders;
            _lines = lines;
        }

        public Dictionary<string, Customer> Customers { get; }
        public Dictionary<string, MeasurementProfile> Measurements { get; }
        public Dictionary<string, Order> Orders { get; }

        public bool HasCustomer(string id) => Customers.ContainsKey(id);

        public List<OrderLine> LinesFor(string orderId) => _lines.TryGetValue(orderId, out var l) ? l : [];

        public void SetLines(string orderId, List<OrderLine> lines) => _lines[orderId] = lines;

        public static async Task<LocalState> LoadAsync(
            LocalDbContext db,
            string businessId,
            IEnumerable<string?> customerIds,
            IEnumerable<string?> measurementIds,
            IEnumerable<string?> orderIds,
            CancellationToken ct)
        {
            var cIds = customerIds.OfType<string>().Distinct().ToList();
            var mIds = measurementIds.OfType<string>().Distinct().ToList();
            var oIds = orderIds.OfType<string>().Distinct().ToList();
            var customers = cIds.Count == 0 ? [] : await db.Customers.Where(c => c.BusinessId == businessId && cIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
            var measurements = mIds.Count == 0 ? [] : await db.MeasurementProfiles
                .Where(m => m.BusinessId == businessId && mIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, ct).ConfigureAwait(false);
            var orders = oIds.Count == 0 ? [] : await db.Orders.Where(o => o.BusinessId == businessId && oIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, ct).ConfigureAwait(false);
            var lines = oIds.Count == 0 ? [] : (await db.OrderLines.Where(l => l.BusinessId == businessId && oIds.Contains(l.OrderId))
                .ToListAsync(ct).ConfigureAwait(false)).GroupBy(l => l.OrderId).ToDictionary(g => g.Key, g => g.ToList());
            return new LocalState(customers, measurements, orders, lines);
        }
    }
}
