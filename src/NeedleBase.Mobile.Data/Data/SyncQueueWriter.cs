using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Application.Sync;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Sync;

namespace NeedleBase.Mobile.Data.Data;

/// <summary>
/// Auto-queue (mobile-sync rule 2): turns tracked Customer / MeasurementProfile / Order changes into
/// <see cref="SyncQueueItem"/>s inside the same SaveChanges. One item per entity; later edits are
/// coalesced (keeps the first base version, latest payload). A create followed by a delete before
/// sync cancels out. Order lines travel inside their order's payload. Payments queue as upserts only
/// (record, then the one-way void); their ParentId is the order.
/// </summary>
internal static class SyncQueueWriter
{
    public static async Task<bool> EnqueueAsync(LocalDbContext db, CancellationToken cancellationToken)
    {
        var entries = db.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                        e.Entity is Customer or MeasurementProfile or Order or Payment)
            .ToList();
        if (entries.Count == 0)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var queued = false;
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Modified && !HasRealChange(entry))
            {
                continue; // skip no-op patches
            }

            var (type, id, businessId, parentId, label, payload) = Describe(entry);
            var existing = db.SyncQueue.Local.FirstOrDefault(q => q.BusinessId == businessId && q.EntityType == type && q.EntityId == id)
                ?? await db.SyncQueue.FirstOrDefaultAsync(
                    q => q.BusinessId == businessId && q.EntityType == type && q.EntityId == id,
                    cancellationToken).ConfigureAwait(false);

            if (entry.State == EntityState.Deleted)
            {
                if (existing is not null && existing.Operation == SyncOperation.Upsert && existing.BaseModifiedUtc is null)
                {
                    // Created and deleted before it ever reached the server: nothing to send.
                    db.SyncQueue.Remove(existing);
                }
                else if (existing is not null)
                {
                    existing.Operation = SyncOperation.Delete;
                    existing.PayloadJson = null;
                    existing.State = SyncQueueState.Pending;
                    existing.LastError = null;
                    existing.ConflictServerJson = null;
                }
                else
                {
                    db.SyncQueue.Add(NewItem(type, id, businessId, parentId, label, SyncOperation.Delete, null,
                        OriginalModified(entry), now));
                }

                if (type == SyncEntityType.Customer)
                {
                    // The server cascades a customer's measurements; drop their queued changes.
                    var children = await db.SyncQueue
                        .Where(q => q.BusinessId == businessId && q.ParentId == id && q.EntityType == SyncEntityType.Measurement)
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    db.SyncQueue.RemoveRange(children);
                }

                queued = true;
                continue;
            }

            if (existing is null)
            {
                var baseModified = entry.State == EntityState.Added ? null : OriginalModified(entry);
                db.SyncQueue.Add(NewItem(type, id, businessId, parentId, label, SyncOperation.Upsert, payload, baseModified, now));
            }
            else
            {
                if (type == SyncEntityType.Order && existing.Operation == SyncOperation.Upsert)
                {
                    payload = KeepOrderLines(existing.PayloadJson, payload);
                }

                existing.Operation = SyncOperation.Upsert;
                existing.PayloadJson = payload;
                existing.Label = label;
                if (existing.State == SyncQueueState.Failed)
                {
                    // The user changed the record: give it another go.
                    existing.State = SyncQueueState.Pending;
                    existing.RetryCount = 0;
                    existing.LastError = null;
                }
            }

            queued = true;
        }

        return queued;
    }

    private static bool HasRealChange(EntityEntry entry) =>
        entry.Properties.Any(p => p.IsModified &&
            p.Metadata.Name is not (LocalDbContext.SearchNameColumn or LocalDbContext.PhoneDigitsColumn or LocalDbContext.ServerKnownColumn) &&
            !Equals(p.OriginalValue, p.CurrentValue));

    private static DateTimeOffset? OriginalModified(EntityEntry entry) =>
        entry.State == EntityState.Added ? null : (DateTimeOffset?)entry.OriginalValues["DateModifiedUtc"];

    /// <summary>A create item for a row that exists locally but was never queued (adoption; see LocalDbContext.ServerKnownColumn).</summary>
    internal static SyncQueueItem CreateItemFor(object entity, DateTimeOffset now)
    {
        var (type, id, businessId, parentId, label, payload) = Describe(entity);
        return NewItem(type, id, businessId, parentId, label, SyncOperation.Upsert, payload, null, now);
    }

    private static (string Type, string Id, string BusinessId, string? ParentId, string Label, string Payload) Describe(EntityEntry entry) =>
        Describe(entry.Entity);

    private static (string Type, string Id, string BusinessId, string? ParentId, string Label, string Payload) Describe(object entity) =>
        entity switch
        {
            Customer c => (SyncEntityType.Customer, c.Id, c.BusinessId, null, c.Name, SyncJson.Serialize(SyncMapper.ToDto(c))),
            MeasurementProfile m => (SyncEntityType.Measurement, m.Id, m.BusinessId, m.CustomerId,
                GarmentTemplate.DisplayName(m.GarmentTemplate) + " measurements", SyncJson.Serialize(SyncMapper.ToDto(m))),
            Order o => (SyncEntityType.Order, o.Id, o.BusinessId, o.CustomerId, "Order #" + o.DisplayRef,
                SyncJson.Serialize(SyncMapper.ToDto(o, o.Lines))),
            Payment p => (SyncEntityType.Payment, p.Id, p.BusinessId, p.OrderId,
                "Payment " + NeedleBase.Application.Contracts.Payments.Money.Format(p.Amount),
                SyncJson.Serialize(SyncMapper.ToDto(p))),
            _ => throw new InvalidOperationException("Not a synced entity.")
        };

    /// <summary>Status/header edits don't load lines; keep the lines from the queued create.</summary>
    private static string KeepOrderLines(string? previousJson, string latestJson)
    {
        var previous = SyncJson.Deserialize<SyncOrderDto>(previousJson);
        var latest = SyncJson.Deserialize<SyncOrderDto>(latestJson)!;
        return previous is not null && latest.Lines.Count == 0 && previous.Lines.Count > 0
            ? SyncJson.Serialize(latest with { Lines = previous.Lines })
            : latestJson;
    }

    private static SyncQueueItem NewItem(
        string type,
        string id,
        string businessId,
        string? parentId,
        string label,
        string operation,
        string? payload,
        DateTimeOffset? baseModified,
        DateTimeOffset now) => new()
    {
        Id = IdGenerator.NewId(),
        BusinessId = businessId,
        EntityType = type,
        EntityId = id,
        ParentId = parentId,
        Operation = operation,
        PayloadJson = payload,
        BaseModifiedUtc = baseModified,
        Label = label,
        CreatedAtUtc = now,
        State = SyncQueueState.Pending
    };
}
