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

/// <summary>Never-synced record being discarded, and the local children that go with it.</summary>
public sealed record DiscardImpact(bool NeverSynced, int Measurements, int Orders)
{
    public int Children => Measurements + Orders;
}

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
        await AdoptAsync(businessId, cancellationToken).ConfigureAwait(false);
        await PushAsync(businessId, cancellationToken).ConfigureAwait(false);

        if (await IsResyncPendingAsync(businessId, cancellationToken).ConfigureAwait(false))
        {
            await ResyncAsync(businessId, cancellationToken).ConfigureAwait(false);
            await PushAsync(businessId, cancellationToken).ConfigureAwait(false); // rows adopted by the resync
            return;
        }

        try
        {
            await PullAsync(businessId, cancellationToken).ConfigureAwait(false);
        }
        catch (SyncCursorInvalidException ex)
        {
            // Expired cursor, or a new server epoch (database restored/moved): start over from scratch, once.
            await ResyncAsync(businessId, recover: ex.ServerReset, cancellationToken).ConfigureAwait(false);
            await PushAsync(businessId, cancellationToken).ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------ adoption (rows from pre-sync builds)

    /// <summary>
    /// Finds rows that are neither server-known nor queued (written by a build without the sync queue, or
    /// pulled before schema v6). On a device that never synced this business they can only be local, so they
    /// are queued as creates. Otherwise one full resync decides: rows the server sends become server-known,
    /// the rest are queued as creates (see <see cref="PruneAsync"/>). Safe to call every run: once every row is
    /// known or queued it is a single indexed query.
    /// </summary>
    private async Task AdoptAsync(string businessId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var customers = await UnadoptedCustomers(db, businessId).Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        var measurements = await UnadoptedMeasurements(db, businessId).Select(m => m.Id).ToListAsync(ct).ConfigureAwait(false);
        var orders = await UnadoptedOrders(db, businessId).Select(o => o.Id).ToListAsync(ct).ConfigureAwait(false);
        var payments = await UnadoptedPayments(db, businessId).Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false);
        if (customers.Count + measurements.Count + orders.Count + payments.Count == 0)
        {
            return;
        }

        var info = await GetOrAddDeviceInfoAsync(db, businessId, ct).ConfigureAwait(false);
        db.SuppressSyncQueue = true;
        try
        {
            if (info.PullCursor is null && info.LastSyncUtc is null && !info.ResyncPending)
            {
                await QueueCreatesAsync(db, businessId, customers, measurements, orders, ct, paymentIds: payments).ConfigureAwait(false);
            }
            else
            {
                info.ResyncPending = true;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            db.SuppressSyncQueue = false;
        }

        LocalSyncSignals.RaiseQueueChanged();
    }

    private static IQueryable<Customer> UnadoptedCustomers(LocalDbContext db, string businessId) =>
        db.Customers.Where(c => c.BusinessId == businessId && !EF.Property<bool>(c, LocalDbContext.ServerKnownColumn) &&
            !db.SyncQueue.Any(q => q.BusinessId == businessId && q.EntityType == SyncEntityType.Customer && q.EntityId == c.Id));

    private static IQueryable<MeasurementProfile> UnadoptedMeasurements(LocalDbContext db, string businessId) =>
        db.MeasurementProfiles.Where(m => m.BusinessId == businessId && !EF.Property<bool>(m, LocalDbContext.ServerKnownColumn) &&
            !db.SyncQueue.Any(q => q.BusinessId == businessId && q.EntityType == SyncEntityType.Measurement && q.EntityId == m.Id));

    private static IQueryable<Order> UnadoptedOrders(LocalDbContext db, string businessId) =>
        db.Orders.Where(o => o.BusinessId == businessId && !EF.Property<bool>(o, LocalDbContext.ServerKnownColumn) &&
            !db.SyncQueue.Any(q => q.BusinessId == businessId && q.EntityType == SyncEntityType.Order && q.EntityId == o.Id));

    private static IQueryable<Payment> UnadoptedPayments(LocalDbContext db, string businessId) =>
        db.Payments.Where(p => p.BusinessId == businessId && !EF.Property<bool>(p, LocalDbContext.ServerKnownColumn) &&
            !db.SyncQueue.Any(q => q.BusinessId == businessId && q.EntityType == SyncEntityType.Payment && q.EntityId == p.Id));

    private static IQueryable<Payment> UnqueuedPayments(LocalDbContext db, string businessId) =>
        db.Payments.Where(p => p.BusinessId == businessId &&
            !db.SyncQueue.Any(q => q.BusinessId == businessId && q.EntityType == SyncEntityType.Payment && q.EntityId == p.Id));

    private static IQueryable<Customer> UnqueuedCustomers(LocalDbContext db, string businessId) =>
        db.Customers.Where(c => c.BusinessId == businessId &&
            !db.SyncQueue.Any(q => q.BusinessId == businessId && q.EntityType == SyncEntityType.Customer && q.EntityId == c.Id));

    private static IQueryable<MeasurementProfile> UnqueuedMeasurements(LocalDbContext db, string businessId) =>
        db.MeasurementProfiles.Where(m => m.BusinessId == businessId &&
            !db.SyncQueue.Any(q => q.BusinessId == businessId && q.EntityType == SyncEntityType.Measurement && q.EntityId == m.Id));

    private static IQueryable<Order> UnqueuedOrders(LocalDbContext db, string businessId) =>
        db.Orders.Where(o => o.BusinessId == businessId &&
            !db.SyncQueue.Any(q => q.BusinessId == businessId && q.EntityType == SyncEntityType.Order && q.EntityId == o.Id));

    /// <summary>Queues creates for the given unqueued rows (skips any that gained a queue item meanwhile).</summary>
    private static async Task QueueCreatesAsync(LocalDbContext db, string businessId, IReadOnlyCollection<string> customerIds,
        IReadOnlyCollection<string> measurementIds, IReadOnlyCollection<string> orderIds, CancellationToken ct, bool includeKnown = false,
        IReadOnlyCollection<string>? paymentIds = null)
    {
        // includeKnown: recover mode, where server-known rows the restored server lacks are re-uploaded too.
        var now = DateTimeOffset.UtcNow;
        var entities = new List<object>();
        if (customerIds.Count > 0)
        {
            entities.AddRange(await (includeKnown ? UnqueuedCustomers(db, businessId) : UnadoptedCustomers(db, businessId)).Where(c => customerIds.Contains(c.Id)).ToListAsync(ct).ConfigureAwait(false));
        }

        if (measurementIds.Count > 0)
        {
            entities.AddRange(await (includeKnown ? UnqueuedMeasurements(db, businessId) : UnadoptedMeasurements(db, businessId)).Where(m => measurementIds.Contains(m.Id)).ToListAsync(ct).ConfigureAwait(false));
        }

        if (orderIds.Count > 0)
        {
            entities.AddRange(await (includeKnown ? UnqueuedOrders(db, businessId) : UnadoptedOrders(db, businessId)).Where(o => orderIds.Contains(o.Id)).Include(o => o.Lines)
                .ToListAsync(ct).ConfigureAwait(false));
        }

        if (paymentIds is { Count: > 0 })
        {
            entities.AddRange(await (includeKnown ? UnqueuedPayments(db, businessId) : UnadoptedPayments(db, businessId))
                .Where(p => paymentIds.Contains(p.Id)).ToListAsync(ct).ConfigureAwait(false));
        }

        foreach (var entity in entities)
        {
            db.SyncQueue.Add(SyncQueueWriter.CreateItemFor(entity, now));
        }
    }

    private static async Task MarkServerKnownAsync(LocalDbContext db, string businessId, IEnumerable<string> customerIds,
        IEnumerable<string> measurementIds, IEnumerable<string> orderIds, CancellationToken ct)
    {
        var c = customerIds.ToList();
        var m = measurementIds.ToList();
        var o = orderIds.ToList();
        if (c.Count > 0)
        {
            await db.Customers.Where(x => x.BusinessId == businessId && c.Contains(x.Id) && !EF.Property<bool>(x, LocalDbContext.ServerKnownColumn))
                .ExecuteUpdateAsync(s => s.SetProperty(x => EF.Property<bool>(x, LocalDbContext.ServerKnownColumn), true), ct).ConfigureAwait(false);
        }

        if (m.Count > 0)
        {
            await db.MeasurementProfiles.Where(x => x.BusinessId == businessId && m.Contains(x.Id) && !EF.Property<bool>(x, LocalDbContext.ServerKnownColumn))
                .ExecuteUpdateAsync(s => s.SetProperty(x => EF.Property<bool>(x, LocalDbContext.ServerKnownColumn), true), ct).ConfigureAwait(false);
        }

        if (o.Count > 0)
        {
            await db.Orders.Where(x => x.BusinessId == businessId && o.Contains(x.Id) && !EF.Property<bool>(x, LocalDbContext.ServerKnownColumn))
                .ExecuteUpdateAsync(s => s.SetProperty(x => EF.Property<bool>(x, LocalDbContext.ServerKnownColumn), true), ct).ConfigureAwait(false);
        }
    }

    private static async Task MarkPaymentsKnownAsync(LocalDbContext db, string businessId, IEnumerable<string> paymentIds, CancellationToken ct)
    {
        var ids = paymentIds.Distinct().ToList();
        if (ids.Count > 0)
        {
            await db.Payments.Where(x => x.BusinessId == businessId && ids.Contains(x.Id) && !EF.Property<bool>(x, LocalDbContext.ServerKnownColumn))
                .ExecuteUpdateAsync(s => s.SetProperty(x => EF.Property<bool>(x, LocalDbContext.ServerKnownColumn), true), ct).ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------ push

    public async Task PushAsync(string businessId, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            List<SyncQueueItem> batch;
            string? deviceId;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
                // Parents first (a restored customer must exist before its order), then queue order.
                batch = await db.SyncQueue.AsNoTracking()
                    .Where(q => q.BusinessId == businessId && q.State == SyncQueueState.Pending)
                    .OrderBy(q => q.EntityType == SyncEntityType.Customer ? 0 : q.EntityType == SyncEntityType.Measurement ? 1
                        : q.EntityType == SyncEntityType.Payment ? 3 : 2) // payments after their orders
                    .ThenBy(q => q.Id)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                // The phone's id labels its changes in the server's activity log; the first sync of a fresh
                // install creates it here (same defaults the pull would give it).
                deviceId = batch.Count == 0 ? null : (await GetOrAddDeviceInfoAsync(db, businessId, cancellationToken).ConfigureAwait(false)).DeviceId;
                if (db.ChangeTracker.HasChanges())
                {
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            if (batch.Count == 0)
            {
                return;
            }

            var request = new SyncPushRequest { Operations = batch.Select(ToOperation).ToList(), DeviceId = deviceId };
            var response = await WithRateLimitWaitAsync(ct => _api.PushAsync(request, ct), cancellationToken).ConfigureAwait(false);
            await ApplyPushResultsAsync(businessId, batch, response, cancellationToken).ConfigureAwait(false);

            if (batch.Count < BatchSize)
            {
                return;
            }
        }
    }

    /// <summary>Longest Retry-After the engine sits out inside a run; longer ones end the run (normal backoff).</summary>
    public static readonly TimeSpan MaxInlineRateLimitWait = TimeSpan.FromSeconds(30);

    /// <summary>Waits between attempts are capped by <see cref="MaxInlineRateLimitWait"/>; tests shorten them.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    /// <summary>
    /// A 429 means "not processed, slow down": wait Retry-After and send the same request again, so a big first
    /// pull keeps going (pages already applied stay applied). Gives up after a few tries.
    /// </summary>
    private async Task<T> WithRateLimitWaitAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call(ct).ConfigureAwait(false);
            }
            catch (SyncTransientException ex) when (ex.RateLimited && attempt < 10 &&
                                                    (ex.RetryAfter ?? TimeSpan.FromSeconds(1)) <= MaxInlineRateLimitWait)
            {
                await Delay(ex.RetryAfter ?? TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
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
                case SyncEntityType.Payment:
                    op.Payment = SyncJson.Deserialize<SyncPaymentDto>(item.PayloadJson);
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
            var paymentCopies = new List<SyncPaymentDto>();

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
                            if (result.Payment is not null)
                            {
                                paymentCopies.Add(result.Payment); // e.g. the server's void wins over this device's copy
                            }

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
                        item.LastError = item.EntityType == SyncEntityType.Payment && result.Code == SyncErrorCodes.ParentDeleted
                            ? "Its order was deleted on another device."
                            : ConflictText(result.Code);
                        item.ConflictServerJson = result.Customer is not null ? SyncJson.Serialize(result.Customer)
                            : result.Measurement is not null ? SyncJson.Serialize(result.Measurement)
                            : result.Order is not null ? SyncJson.Serialize(result.Order)
                            : result.Payment is not null ? SyncJson.Serialize(result.Payment)
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

            await ApplyPaymentCopiesAsync(db, businessId, paymentCopies, cancellationToken).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var appliedUpserts = sent.Where(s => s.Operation == SyncOperation.Upsert &&
                results.TryGetValue(s.Id, out var r) && r.Status == SyncOpStatus.Applied).ToList();
            await MarkServerKnownAsync(db, businessId,
                appliedUpserts.Where(s => s.EntityType == SyncEntityType.Customer).Select(s => s.EntityId),
                appliedUpserts.Where(s => s.EntityType == SyncEntityType.Measurement).Select(s => s.EntityId),
                appliedUpserts.Where(s => s.EntityType == SyncEntityType.Order).Select(s => s.EntityId),
                cancellationToken).ConfigureAwait(false);
            await MarkPaymentsKnownAsync(db, businessId,
                appliedUpserts.Where(s => s.EntityType == SyncEntityType.Payment).Select(s => s.EntityId), cancellationToken).ConfigureAwait(false);
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
        SyncErrorCodes.DeleteRefused => "Still in use on the server, so it wasn't deleted.",
        SyncErrorCodes.PaymentMismatch => "A different payment was saved elsewhere. NeedleBase will not discard a payment without your choice.",
        NeedleBase.Application.Contracts.Errors.OrderErrorMessages.NotAssigned =>
            "Saved, but the status wasn't changed: this order is assigned to someone else. Use the shop's copy.",
        not null when code.StartsWith("ORDER_", StringComparison.Ordinal) =>
            "Saved, but the status change wasn't allowed. Use the shop's copy.",
        _ => "Changed on another device."
    };

    private static DateTimeOffset? ServerModified(SyncPushResult result) =>
        result.Customer?.DateModifiedUtc ?? result.Measurement?.DateModifiedUtc ?? result.Order?.DateModifiedUtc ?? result.Payment?.DateModifiedUtc;

    // ------------------------------------------------------------------ pull

    public Task PullAsync(string businessId, CancellationToken cancellationToken = default) =>
        PullCoreAsync(businessId, seen: null, cancellationToken);

    private async Task PullCoreAsync(string businessId, SeenIds? seen, CancellationToken cancellationToken)
    {
        string? cursor = null;
        string? deviceId;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            var info = await db.DeviceInfo.AsNoTracking()
                .FirstOrDefaultAsync(d => d.BusinessId == businessId, cancellationToken).ConfigureAwait(false);
            deviceId = info?.DeviceId is { Length: > 0 } id ? id : null; // activity log label on the server
            if (seen is null)
            {
                cursor = info?.PullCursor;
            }
        }

        while (true)
        {
            var request = new SyncPullRequest { Cursor = cursor, Limit = BatchSize, DeviceId = deviceId };
            var page = await WithRateLimitWaitAsync(ct => _api.PullAsync(request, ct), cancellationToken).ConfigureAwait(false);
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
    public Task ResyncAsync(string businessId, CancellationToken cancellationToken = default) =>
        ResyncAsync(businessId, recover: false, cancellationToken);

    /// <param name="recover">
    /// After a server restore (new epoch): rows the server lacks are re-uploaded (queued as creates), never
    /// pruned. Sticky in <see cref="DeviceInfo.ResyncRecover"/> until the resync completes.
    /// </param>
    public async Task ResyncAsync(string businessId, bool recover, CancellationToken cancellationToken = default)
    {
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            var info = await GetOrAddDeviceInfoAsync(db, businessId, cancellationToken).ConfigureAwait(false);
            info.ResyncPending = true;
            info.ResyncRecover |= recover;
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

            var pagePayments = page.Payments ?? [];
            var deferredKeys = page.Measurements.Select(m => m.Id).Concat(page.Orders.Select(o => o.Id))
                .Concat(pagePayments.Select(p => p.Id)).ToList();
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

            var changedRows = HasRowChanges(db);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await MarkServerKnownAsync(db, businessId, page.Customers.Select(c => c.Id), page.Measurements.Select(m => m.Id),
                page.Orders.Select(o => o.Id), cancellationToken).ConfigureAwait(false);

            changedRows |= page.Deleted.Count > 0 || pruneWith is not null;
            if (page.Deleted.Count > 0)
            {
                await ApplyDeletesAsync(db, state, businessId, page.Deleted, queued, queue, cancellationToken).ConfigureAwait(false);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            // Payments (phase 4) after their orders; one whose order isn't here yet waits like any other child.
            var incoming = pagePayments.Where(p => !queued.ContainsKey(SyncEntityType.Payment + ":" + p.Id)).ToList();
            if (incoming.Count > 0)
            {
                var orderIds = incoming.Select(p => p.OrderId).Distinct().ToList();
                var localOrders = (await db.Orders.Where(o => o.BusinessId == businessId && orderIds.Contains(o.Id))
                    .Select(o => o.Id).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
                var ready = new List<SyncPaymentDto>();
                foreach (var payment in incoming)
                {
                    if (!localOrders.Contains(payment.OrderId))
                    {
                        Defer(db, existingDeferred, businessId, SyncEntityType.Payment, payment.Id, payment.OrderId, SyncJson.Serialize(payment));
                        continue;
                    }

                    ready.Add(payment);
                    Undefer(db, existingDeferred, SyncEntityType.Payment, payment.Id);
                }

                await ApplyPaymentCopiesAsync(db, businessId, ready, cancellationToken).ConfigureAwait(false);
                changedRows |= HasRowChanges(db);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await MarkPaymentsKnownAsync(db, businessId, ready.Select(p => p.Id), cancellationToken).ConfigureAwait(false);
            }

            await ApplyDeferredAsync(db, businessId, cancellationToken).ConfigureAwait(false);
            changedRows |= HasRowChanges(db);

            // Cursor saved in the same transaction as the page: a kill can't leave them out of step.
            var info = await GetOrAddDeviceInfoAsync(db, businessId, cancellationToken).ConfigureAwait(false);
            if (pruneWith is not null)
            {
                await PruneAsync(db, businessId, pruneWith, info.ResyncRecover, cancellationToken).ConfigureAwait(false);
            }

            info.PullCursor = page.Cursor;
            if (!page.HasMore)
            {
                info.LastSyncUtc = DateTimeOffset.UtcNow;
                if (pruneWith is not null)
                {
                    info.ResyncPending = false;
                    info.ResyncRecover = false;
                }
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (changedRows)
            {
                LocalDataVersion.NotifyChangedBySync(businessId); // visible lists reload (debounced) after the commit
            }
        }
        finally
        {
            db.SuppressSyncQueue = false;
        }
    }

    /// <summary>True when the tracker holds an insert/update/delete of a row a list shows (not cursor/queue rows).</summary>
    private static bool HasRowChanges(LocalDbContext db) =>
        db.ChangeTracker.Entries().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
            e.Entity is Customer or MeasurementProfile or Order or OrderLine or Payment);

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
            .Where(d => d.BusinessId == businessId && d.EntityType != SyncEntityType.Payment &&
                        db.Customers.Any(c => c.BusinessId == businessId && c.Id == d.ParentId))
            .ToListAsync(ct).ConfigureAwait(false);
        if (ready.Count == 0)
        {
            await ApplyDeferredPaymentsAsync(db, businessId, ct).ConfigureAwait(false);
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
        await ApplyDeferredPaymentsAsync(db, businessId, ct).ConfigureAwait(false); // their orders may have just arrived
    }

    /// <summary>Applies deferred payments whose order is now on this device. Caller sets SuppressSyncQueue.</summary>
    private static async Task ApplyDeferredPaymentsAsync(LocalDbContext db, string businessId, CancellationToken ct)
    {
        var ready = await db.SyncDeferred
            .Where(d => d.BusinessId == businessId && d.EntityType == SyncEntityType.Payment &&
                        db.Orders.Any(o => o.BusinessId == businessId && o.Id == d.ParentId))
            .ToListAsync(ct).ConfigureAwait(false);
        if (ready.Count == 0)
        {
            return;
        }

        var queuedIds = await db.SyncQueue.Where(q => q.BusinessId == businessId && q.EntityType == SyncEntityType.Payment)
            .Select(q => q.EntityId).ToListAsync(ct).ConfigureAwait(false);
        var payments = ready.Select(d => SyncJson.Deserialize<SyncPaymentDto>(d.PayloadJson)!)
            .Where(p => !queuedIds.Contains(p.Id)).ToList();
        await ApplyPaymentCopiesAsync(db, businessId, payments, ct).ConfigureAwait(false);
        db.SyncDeferred.RemoveRange(ready);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await MarkPaymentsKnownAsync(db, businessId, payments.Select(p => p.Id), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Upserts server payment copies (immutable except the one-way void, so the server copy is simply taken).
    /// Caller sets SuppressSyncQueue and makes sure each order is on this device.
    /// </summary>
    private static async Task ApplyPaymentCopiesAsync(LocalDbContext db, string businessId, IReadOnlyCollection<SyncPaymentDto> payments, CancellationToken ct)
    {
        if (payments.Count == 0)
        {
            return;
        }

        var ids = payments.Select(p => p.Id).Distinct().ToList();
        var local = await db.Payments.Where(p => p.BusinessId == businessId && ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        foreach (var dto in payments)
        {
            if (!local.TryGetValue(dto.Id, out var entity))
            {
                try
                {
                    entity = Payment.Create(dto.Id, businessId, dto.OrderId, dto.CustomerId, dto.Amount, dto.Method, dto.Note,
                        dto.RecordedAtUtc, dto.CreatedAtUtc);
                }
                catch (Exception ex) when (ex is ArgumentException or NeedleBase.Domain.Exceptions.DomainRuleException)
                {
                    continue; // a row this build can't represent (e.g. a newer method): never crash the sync
                }

                db.Payments.Add(entity);
                local[entity.Id] = entity;
            }

            var entry = db.Entry(entity);
            entry.Property(p => p.OrderId).CurrentValue = dto.OrderId;
            entry.Property(p => p.CustomerId).CurrentValue = dto.CustomerId;
            entry.Property(p => p.Amount).CurrentValue = dto.Amount;
            entry.Property(p => p.Method).CurrentValue = dto.Method;
            entry.Property(p => p.Note).CurrentValue = dto.Note;
            entry.Property(p => p.RecordedAtUtc).CurrentValue = dto.RecordedAtUtc;
            entry.Property(p => p.IsVoided).CurrentValue = dto.IsVoided;
            entry.Property(p => p.VoidedAtUtc).CurrentValue = dto.VoidedAtUtc;
            entry.Property(p => p.VoidReason).CurrentValue = dto.VoidReason;
            entry.Property(p => p.CreatedAtUtc).CurrentValue = dto.CreatedAtUtc;
            entry.Property(p => p.DateModifiedUtc).CurrentValue = dto.DateModifiedUtc;
            entry.Property(LocalDbContext.ServerKnownColumn).CurrentValue = true;
        }
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
                        if (queue.Any(q => q.ParentId == order.Id && q.EntityType == SyncEntityType.Payment && db.Entry(q).State != EntityState.Deleted))
                        {
                            // Payments recorded here aren't on the server yet: never drop them with the order. Ask the user.
                            var orderLines = await db.OrderLines.Where(l => l.BusinessId == businessId && l.OrderId == order.Id)
                                .ToListAsync(ct).ConfigureAwait(false);
                            var payload = SyncJson.Serialize(Application.Sync.SyncMapper.ToDto(order, orderLines));
                            var conflict = item is not null && db.Entry(item).State != EntityState.Deleted ? item : null;
                            if (conflict is null)
                            {
                                conflict = NewQueueItem(businessId, SyncEntityType.Order, order.Id, order.CustomerId, "Order #" + order.DisplayRef, payload);
                                db.SyncQueue.Add(conflict);
                                queue.Add(conflict);
                                queued[SyncEntityType.Order + ":" + order.Id] = conflict;
                            }

                            conflict.Operation = SyncOperation.Upsert;
                            conflict.PayloadJson ??= payload;
                            conflict.BaseModifiedUtc = null;
                            MarkDeletedElsewhere(conflict, "Deleted on another device. It has payments recorded on this device.");
                            break;
                        }

                        db.Orders.Remove(order); // lines (and server-known payments, which the server never orphans) cascade locally
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

    /// <summary>
    /// After a full pull: rows the server didn't send and that have no queued change are either gone on the
    /// server (server-known: delete here) or were never synced (not server-known: queue them as creates, so
    /// work from pre-sync builds is uploaded rather than lost). A customer stays while anything kept needs it.
    /// </summary>
    private static async Task PruneAsync(LocalDbContext db, string businessId, SeenIds seen, bool recover, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var queue = await db.SyncQueue.AsNoTracking().Where(q => q.BusinessId == businessId)
            .Select(q => new { q.EntityId, q.ParentId }).ToListAsync(ct).ConfigureAwait(false);
        var keep = queue.Select(q => q.EntityId).Concat(queue.Where(q => q.ParentId != null).Select(q => q.ParentId!))
            .ToHashSet(StringComparer.Ordinal);

        var orders = (await db.Orders.Where(o => o.BusinessId == businessId)
            .Select(o => new { o.Id, o.CustomerId, Known = !recover && EF.Property<bool>(o, LocalDbContext.ServerKnownColumn) })
            .ToListAsync(ct).ConfigureAwait(false)).Where(o => !seen.Orders.Contains(o.Id) && !keep.Contains(o.Id)).ToList();
        var measurements = (await db.MeasurementProfiles.Where(m => m.BusinessId == businessId)
            .Select(m => new { m.Id, m.CustomerId, Known = !recover && EF.Property<bool>(m, LocalDbContext.ServerKnownColumn) })
            .ToListAsync(ct).ConfigureAwait(false)).Where(m => !seen.Measurements.Contains(m.Id) && !keep.Contains(m.Id)).ToList();
        var payments = (await db.Payments.Where(p => p.BusinessId == businessId)
            .Select(p => new { p.Id, p.OrderId, Known = !recover && EF.Property<bool>(p, LocalDbContext.ServerKnownColumn) })
            .ToListAsync(ct).ConfigureAwait(false)).Where(p => !seen.Payments.Contains(p.Id) && !keep.Contains(p.Id)).ToList();
        var deletePayments = payments.Where(p => p.Known).Select(p => p.Id).ToList();
        var adoptPayments = payments.Where(p => !p.Known).ToList();
        // An order stays while a payment that isn't being pruned still hangs off it (the local FK cascades).
        var deletePaymentSet = deletePayments.ToHashSet(StringComparer.Ordinal);
        var ordersWithKeptPayments = (await db.Payments.Where(p => p.BusinessId == businessId).Select(p => new { p.Id, p.OrderId })
            .ToListAsync(ct).ConfigureAwait(false)).Where(p => !deletePaymentSet.Contains(p.Id)).Select(p => p.OrderId)
            .ToHashSet(StringComparer.Ordinal);
        var deleteOrders = orders.Where(o => o.Known && !ordersWithKeptPayments.Contains(o.Id)).Select(o => o.Id).ToList();
        var adoptOrders = orders.Where(o => !o.Known).Select(o => o.Id).ToList();
        var deleteMeasurements = measurements.Where(m => m.Known).Select(m => m.Id).ToList();
        var adoptMeasurements = measurements.Where(m => !m.Known).ToList();

        var neededCustomers = (await db.Orders.Where(o => o.BusinessId == businessId && !deleteOrders.Contains(o.Id))
            .Select(o => o.CustomerId).ToListAsync(ct).ConfigureAwait(false))
            .Concat(adoptMeasurements.Select(m => m.CustomerId))
            .ToHashSet(StringComparer.Ordinal);
        var customers = (await db.Customers.Where(c => c.BusinessId == businessId)
            .Select(c => new { c.Id, Known = !recover && EF.Property<bool>(c, LocalDbContext.ServerKnownColumn) })
            .ToListAsync(ct).ConfigureAwait(false)).Where(c => !seen.Customers.Contains(c.Id) && !keep.Contains(c.Id)).ToList();
        var adoptCustomers = customers.Where(c => !c.Known).Select(c => c.Id).ToList();
        var deleteCustomers = customers.Where(c => c.Known && !neededCustomers.Contains(c.Id)).Select(c => c.Id).ToList();

        await db.Payments.Where(p => p.BusinessId == businessId && deletePayments.Contains(p.Id)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.Orders.Where(o => o.BusinessId == businessId && deleteOrders.Contains(o.Id)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.MeasurementProfiles.Where(m => m.BusinessId == businessId && deleteMeasurements.Contains(m.Id)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.Customers.Where(c => c.BusinessId == businessId && deleteCustomers.Contains(c.Id)).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await QueueCreatesAsync(db, businessId, adoptCustomers, adoptMeasurements.Select(m => m.Id).ToList(), adoptOrders, ct, includeKnown: recover,
            paymentIds: adoptPayments.Select(p => p.Id).ToList()).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
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
            entry.Property(LocalDbContext.ServerKnownColumn).CurrentValue = true;
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
            db.Entry(entity).Property(LocalDbContext.ServerKnownColumn).CurrentValue = true;
        }

        if (order is not null && state.HasCustomer(order.CustomerId))
        {
            var lines = order.Lines.Select(l => OrderLine.Create(l.Id, businessId, order.Id, l.GarmentTemplate, l.Quantity, l.UnitPrice,
                l.MeasurementProfileId, l.Unit, l.MeasurementFieldsJson, l.Notes, l.SortOrder, l.MeasurementTakenAtUtc)).ToList();
            if (!state.Orders.TryGetValue(order.Id, out var entity))
            {
                entity = Order.Create(order.Id, businessId, order.CustomerId, order.DueDate, order.FittingDate, order.AssignedToUserId,
                    order.Notes, lines, order.CreatedAtUtc);
                db.Orders.Add(entity);
                state.Orders[entity.Id] = entity;
            }
            else
            {
                // Align with the server's lines: it generates line ids, and Owners/Managers edit lines (qty, price, notes,
                // added/removed). Any difference replaces the local set with the server's.
                var localLines = state.LinesFor(order.Id);
                var serverLines = order.Lines.ToDictionary(l => l.Id, StringComparer.Ordinal);
                if (localLines.Count != serverLines.Count ||
                    localLines.Any(l => !serverLines.TryGetValue(l.Id, out var sl) || sl != (NeedleBase.Application.Sync.SyncMapper.ToDto(l) with { MeasurementTakenAtUtc = sl.MeasurementTakenAtUtc }) ||
                                        !SameInstant(sl.MeasurementTakenAtUtc, l.MeasurementTakenAtUtc)))
                {
                    // Diff in place instead of delete-all/reinsert: unchanged rows are untouched, changed rows get
                    // one UPDATE, and only lines that really came or went are inserted/deleted.
                    var byId = localLines.ToDictionary(l => l.Id, StringComparer.Ordinal);
                    var merged = new List<OrderLine>(lines.Count);
                    foreach (var line in lines)
                    {
                        if (byId.Remove(line.Id, out var local))
                        {
                            db.Entry(local).CurrentValues.SetValues(line);
                            merged.Add(local);
                        }
                        else
                        {
                            db.OrderLines.Add(line);
                            merged.Add(line);
                        }
                    }

                    db.OrderLines.RemoveRange(byId.Values);
                    state.SetLines(order.Id, merged);
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
            entry.Property(LocalDbContext.ServerKnownColumn).CurrentValue = true;
        }
    }

    private static bool SameInstant(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b is null : b is not null && Application.Sync.SyncTime.Normalize(a.Value) == Application.Sync.SyncTime.Normalize(b.Value);

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
    /// What <see cref="DiscardAsync"/> would delete besides the change itself: a never-synced customer takes its
    /// measurements and orders on this device with it. Shown to the user before they confirm.
    /// </summary>
    public async Task<DiscardImpact> GetDiscardImpactAsync(string businessId, string itemId, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var item = await db.SyncQueue.AsNoTracking().FirstOrDefaultAsync(q => q.BusinessId == businessId && q.Id == itemId, cancellationToken)
            .ConfigureAwait(false);
        if (item is null)
        {
            return new DiscardImpact(false, 0, 0);
        }

        var neverSynced = item.Operation == SyncOperation.Upsert && item.BaseModifiedUtc is null;
        if (!neverSynced || item.EntityType != SyncEntityType.Customer)
        {
            return new DiscardImpact(neverSynced, 0, 0);
        }

        var measurements = await db.MeasurementProfiles.CountAsync(m => m.BusinessId == businessId && m.CustomerId == item.EntityId, cancellationToken)
            .ConfigureAwait(false);
        var orders = await db.Orders.CountAsync(o => o.BusinessId == businessId && o.CustomerId == item.EntityId, cancellationToken)
            .ConfigureAwait(false);
        return new DiscardImpact(true, measurements, orders);
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
                case SyncErrorCodes.PaymentMismatch:
                {
                    // Same id, different payment on the server. Keep theirs: take the server copy. Keep mine = "keep both":
                    // this device's payment is re-recorded under a new id, then the server copy takes the old id.
                    var serverPayment = SyncJson.Deserialize<SyncPaymentDto>(item.ConflictServerJson);
                    var mine = await db.Payments.FirstOrDefaultAsync(p => p.BusinessId == businessId && p.Id == item.EntityId, cancellationToken)
                        .ConfigureAwait(false);
                    db.SyncQueue.Remove(item);
                    if (keepMine && mine is not null)
                    {
                        var copy = Payment.Create(IdGenerator.NewId(), businessId, mine.OrderId, mine.CustomerId, mine.Amount, mine.Method,
                            mine.Note, mine.RecordedAtUtc, DateTimeOffset.UtcNow);
                        if (mine.IsVoided)
                        {
                            copy.Void(mine.VoidReason, DateTimeOffset.UtcNow);
                        }

                        db.Payments.Add(copy);
                        db.SyncQueue.Add(SyncQueueWriter.CreateItemFor(copy, DateTimeOffset.UtcNow));
                    }

                    if (serverPayment is not null &&
                        await db.Orders.AnyAsync(o => o.BusinessId == businessId && o.Id == serverPayment.OrderId, cancellationToken).ConfigureAwait(false))
                    {
                        await ApplyPaymentCopiesAsync(db, businessId, [serverPayment], cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        if (mine is not null)
                        {
                            db.Payments.Remove(mine);
                        }

                        (await GetOrAddDeviceInfoAsync(db, businessId, cancellationToken).ConfigureAwait(false)).ResyncPending = true;
                    }

                    break;
                }

                case SyncErrorCodes.ParentDeleted when keepMine && item.EntityType == SyncEntityType.Payment:
                {
                    // Its order was deleted elsewhere: re-create the order (from this device's copy), then the payment.
                    var orderId = item.ParentId;
                    var order = orderId is null
                        ? null
                        : await db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.BusinessId == businessId && o.Id == orderId, cancellationToken)
                            .ConfigureAwait(false);
                    if (order is null)
                    {
                        return false;
                    }

                    var payload = SyncJson.Serialize(Application.Sync.SyncMapper.ToDto(order, order.Lines));
                    var orderItem = await db.SyncQueue.FirstOrDefaultAsync(
                        q => q.BusinessId == businessId && q.EntityType == SyncEntityType.Order && q.EntityId == order.Id, cancellationToken)
                        .ConfigureAwait(false);
                    if (orderItem is null)
                    {
                        db.SyncQueue.Add(NewQueueItem(businessId, SyncEntityType.Order, order.Id, order.CustomerId, "Order #" + order.DisplayRef, payload));
                    }
                    else
                    {
                        Repend(orderItem, baseModified: null);
                        orderItem.Operation = SyncOperation.Upsert;
                        orderItem.PayloadJson = payload;
                    }

                    Repend(item, baseModified: null);
                    break;
                }

                case SyncErrorCodes.DeleteRefused when item.EntityType == SyncEntityType.Order:
                {
                    // The server kept the order (payments were recorded on it): bring it back here.
                    db.SyncQueue.Remove(item);
                    var serverOrder = server.Order;
                    var state = await LocalState.LoadAsync(db, businessId, [serverOrder?.CustomerId], [], [serverOrder?.Id], cancellationToken)
                        .ConfigureAwait(false);
                    ApplyServerCopy(db, state, businessId, null, null, serverOrder);
                    (await GetOrAddDeviceInfoAsync(db, businessId, cancellationToken).ConfigureAwait(false)).ResyncPending = true;
                    break;
                }

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
                await db.SyncQueue.Where(q => q.BusinessId == businessId && q.EntityType == SyncEntityType.Payment &&
                        db.Payments.Any(p => p.BusinessId == businessId && p.Id == q.EntityId && p.CustomerId == id))
                    .ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.Payments.Where(p => p.BusinessId == businessId && p.CustomerId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
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
                // Its payments go with it (local cascade); so do their queued changes.
                await db.SyncQueue.Where(q => q.BusinessId == businessId && q.ParentId == id && q.EntityType == SyncEntityType.Payment)
                    .ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.SyncDeferred.Where(d => d.BusinessId == businessId && d.ParentId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.Payments.Where(p => p.BusinessId == businessId && p.OrderId == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                await db.Orders.Where(o => o.BusinessId == businessId && o.Id == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
                break;
            case SyncEntityType.Payment:
                await db.Payments.Where(p => p.BusinessId == businessId && p.Id == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);
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
            case SyncEntityType.Payment:
                var p = SyncJson.Deserialize<SyncPaymentDto>(item.ConflictServerJson);
                return (p?.DateModifiedUtc, null, null, null);
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
        public HashSet<string> Payments { get; } = new(StringComparer.Ordinal);

        public void Add(SyncPullResponse page)
        {
            Payments.UnionWith((page.Payments ?? []).Select(p => p.Id));
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
