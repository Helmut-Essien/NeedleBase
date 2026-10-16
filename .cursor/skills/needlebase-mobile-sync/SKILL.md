---
name: needlebase-mobile-sync
description: >-
  Implements NeedleBase offline-first SQLite sync: LocalDbContext, sync queue,
  push/pull, conflicts, token refresh honesty, payment-safe merge rules, and
  batched high-throughput sync. Use when changing Mobile.Data, SyncService,
  offline save, DeletedItem pull, or sync UI state wiring.
---

# NeedleBase Mobile Sync (Offline-First)

Learned from AssetTag `LocalDbContext` + `SyncService` + sync skills; adapted for **BusinessId**, multi-entity MVP, and **payment conflict honesty**.

UX copy/states: [needlebase-uiux offline-sync](../needlebase-uiux/offline-sync.md).

## Hard rules

1. **Local-first writes** — Customers, measurements, orders, payments save to SQLite immediately when offline.
2. **Auto-queue** — On `SaveChanges`, enqueue CREATE/PATCH/DELETE into `SyncQueueItem` unless `SuppressSyncQueue` is true.
3. **Pull suppress** — During pull apply, set `SuppressSyncQueue = true` (and/or disable auto-detect) so server data does not re-enqueue. Always reset in `finally`.
4. **Token honesty** — `TokenRefreshResult.IsTransientFailure` must not clear tokens or wipe queue. Background sync fails quietly on transient auth errors.
5. **No duplicate entities** — Push results must be idempotent (client op ids / ULID primary keys). Re-sync after reopen must not create duplicates (Journey 2).
6. **Payments** — Never silently overwrite conflicting payment records. Surface conflict UI; prefer explicit choice or “keep both + flag”.
7. **BusinessId** — Queue items and pull filters always include active business.
8. **DeletedItem** — Server records deletes for pull; mobile removes local rows by type+id+business.
9. **User messaging** — Pending = “Waiting to sync”; success toast after local save = “Saved on this device”.
10. **Speed** — Single orchestrator + semaphores (no concurrent SQLite thrash); batch push/pull (e.g. 200); one `SaveChanges` per batch when safe; skip no-op patches; index queue for pending counts; short scopes via `CreateScope()`; propagate cancellation. Full rules: [code-quality-performance](../needlebase-architecture/code-quality-performance.md).
11. **Inspect before coding** — Follow [engineering-review](../needlebase-architecture/engineering-review.md): inspect first, fix root cause (no parallel workarounds), remove obsolete code, prefer event-driven over unnecessary polling when it fits the architecture, verify with evidence.

## Mobile-only tables (`Mobile.Data`)

| Table | Role |
|-------|------|
| `SyncQueueItem` | Id, BusinessId, EntityType, EntityId, Operation, Payload/Patch, CreatedAt, RetryCount |
| `DeviceInfo` | DeviceId, BusinessId, LastSync (UTC) |

Domain entities mirrored locally: Customer, MeasurementProfile, Order, OrderLine, Payment, (reference data as needed).

## Sync flow

```
User edit → SQLite SaveChanges → SyncQueueItem
     ↓
ISyncService channel (push / full sync)
     ↓
POST api/sync/push  → remove successful ops; retry++ failures
     ↓
GET/POST api/sync/pull since LastSync → apply with SuppressSyncQueue
     ↓
Process DeletedItems → update LastSync
```

### Orchestration tips (AssetTag-proven)

- Single sync orchestrator (often Singleton) with `CreateScope()` per run  
- Semaphores for push vs pull vs full  
- `SyncProgressChanged` event → Home SyncChip / Sync details  
- Background periodic sync with connectivity + battery guards  
- Batch large pulls (e.g. 200 rows)  

### Dependency order on pull (suggested)

1. Business settings / staff roster (if cached)  
2. Customers  
3. Measurement profiles  
4. Orders + lines  
5. Payments  
6. DeletedItems  

## Conflict policy

| Entity | Default |
|--------|---------|
| Customer, measurements, order notes | Field or last-write with user notice if both changed |
| Order status | Prefer merge rules + user confirm if divergent |
| **Payment** | **No silent server-wins**; explicit resolution UI |

Align server `SyncController` with the same rules.

## Sync details screen

List pending ops, last sync time, Retry, failed reasons, conflicts entry — matches UI skill.

## Add-entity sync checklist

1. Map entity in `LocalDbContext`  
2. Include in `QueueSyncOperations` for CREATE/PATCH/DELETE  
3. Add to push DTO + server handler (BusinessId scoped)  
4. Add to pull apply path with suppress flag  
5. Add DeletedItem entity type constant  
6. Expose pending count to SyncChip  
7. Tests: offline create → reopen → sync once  

## Important paths (target)

- `src/NeedleBase.Mobile.Data/Data/LocalDbContext.cs`
- `src/NeedleBase.Mobile/Services/SyncService.cs`
- `src/NeedleBase.Mobile/Services/TokenRefreshHandler.cs`
- `src/NeedleBase.Application/Contracts/.../Sync*.cs`
- `src/NeedleBase.Api` sync endpoints / Infrastructure sync services
