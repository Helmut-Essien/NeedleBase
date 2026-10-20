---
name: needlebase-mobile-sync
description: >-
  Implements NeedleBase offline-first SQLite sync: LocalDbContext, sync queue,
  push/pull, conflicts, token refresh honesty, payment-safe merge rules, and
  batched high-throughput sync (target design, Slice 4). Use when building or
  changing sync, SyncQueueItem, DeletedItem, push/pull endpoints, conflict
  handling, or SyncChip/Sync details wiring.
---

# NeedleBase Mobile Sync (Offline-First)

> **Status: built (Slice 4, 20 Oct 2026) [code].** `SyncQueueItem`/`DeviceInfo` (`Mobile.Data/Data/SyncQueueItem.cs`, migrator v4), enqueue in `LocalDbContext.SaveChanges` via `SyncQueueWriter` (one coalesced item per entity; skipped while `SuppressSyncQueue`), `Mobile.Data/Sync/SyncEngine.cs` (push batches of 200, then cursor pull; apply with `SuppressSyncQueue` reset in `finally`), `Mobile/Services/SyncService.cs` (single orchestrator: start/login, connectivity regained, 2 s after a save, manual; backoff 5 s/15 s/60 s/5 min), `TokenRefreshHandler` (refresh once on 401; transient failure never wipes the queue or forces login). Conflicts: stale base → item `conflict`, local edit kept until the user picks Keep mine / Keep theirs; rule failures → `failed` with the server message. Order line ids come from the server and replace local lines on ack.

**Edge-case hardening (20 Oct 2026) [code]:** migrator v5 (`ConflictCode`, `DeviceInfo.ResyncPending`, `SyncDeferred`). Push sends parents first (customer → measurement → order). Each pull page and its cursor are saved in one transaction; lookups are batched per page. Children whose customer isn't local yet go to `SyncDeferred` and apply when it arrives. Tombstones: ignored if the local row is newer than the delete; a queued delete agrees; a queued edit becomes a `SYNC_DELETED_ELSEWHERE` conflict; a customer with local orders or queued children becomes a conflict instead of a cascade; otherwise the customer, its children, their queue items and deferred rows are removed. Conflict codes: Restore/Delete here (deleted), Restore customer/Discard (`SYNC_PARENT_DELETED`), Restore customer (`SYNC_DELETE_REFUSED`, restores the server copy and resyncs). Failed items have **Discard** (never-synced create → delete locally; otherwise resync). Resync = full pull + prune of rows the server didn't return and that aren't queued; triggered by an invalid cursor, Discard, or delete-refused restore; `ResyncPending` survives app kills. Errors: 400 cursor → resync; other 400 → `failed`/"Sync stopped" (no retry); 401 → "Sign in to sync" (queue kept; re-sign-in to the same user+business keeps local data, a different one wipes it via `local_data_owner`); 403 → "No access" (no retry); others transient with backoff, honouring `Retry-After` (≤1 h); a local save doesn't reset backoff. Token refresh uses one app-wide lock (`AuthService.RefreshIfStaleAsync`).

Built today and still binding: token honesty (`TokenRefreshResult.IsTransientFailure`, tested in `AuthServiceTokenHonestyTests`) and the local services `CustomerLocalService`/`MeasurementLocalService`.

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

### Orchestration

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

## Adoption, epochs, rate limits (second audit, built)

- **ServerKnown** shadow column (Customers/MeasurementProfiles/Orders, migrator v6): set when a row is pulled or its push is acked. `SyncEngine.AdoptAsync` runs first in every run: rows neither known nor queued are queued as creates on a device that never synced, otherwise `ResyncPending` is set and the resync prune decides (unseen + unknown = upload, unseen + known = delete). Excluded from `HasRealChange`.
- **Run order**: Adopt, Push, then Resync (if pending) or Pull; a resync is followed by another Push.
- **Epoch**: `SyncCursorInvalidException.ServerReset` (server code `SYNC_EPOCH_CHANGED`) = server restored. Resync in **recover** mode (`DeviceInfo.ResyncRecover`, sticky until done): every unseen unqueued row is re-uploaded, nothing is pruned. Plain `SYNC_CURSOR_INVALID` keeps normal pruning.
- **429**: `SyncTransientException.RateLimited`; the engine waits Retry-After inline (≤ `MaxInlineRateLimitWait` 30 s, ≤ 10 tries, `Delay` replaceable in tests), longer waits end the run (service backoff).
- **Sign-out**: `await SyncService.StopAsync()` (cancels + awaits the run outside the gate lock, blocks runs until `Start`) before the wipe; restart if the wipe fails.
- **Discard** of a never-synced customer: `GetDiscardImpactAsync` counts children; UI confirms with "Discard all".
- **Other account sign-in**: `AuthService` asks `OtherAccountDataPrompt(pending)` (queued + unadopted rows via `CountUnsyncedAsync`) before storing tokens; no prompt / "no" = refuse and revoke the new refresh token.
- **SQLite**: connection via `LocalDbConnection.Build` (busy timeout 30 s); `LocalStorageErrors` maps disk-full to friendly copy.

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

Use step 6 of the single checklist in [needlebase-domain](../needlebase-domain/SKILL.md#add-entity-checklist-single-source).

## Important paths (target)

- `src/NeedleBase.Mobile.Data/Data/LocalDbContext.cs`
- `src/NeedleBase.Mobile/Services/SyncService.cs`
- `src/NeedleBase.Mobile/Services/TokenRefreshHandler.cs`
- `src/NeedleBase.Application/Contracts/.../Sync*.cs`
- `src/NeedleBase.Api` sync endpoints / Infrastructure sync services
