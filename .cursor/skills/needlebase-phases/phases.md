# NeedleBase slices

Deliver **one slice at a time**. Confirm with the user before starting the next. Every slice must follow [engineering-review](../needlebase-architecture/engineering-review.md) and [code-quality-performance](../needlebase-architecture/code-quality-performance.md). Journey acceptance: [journeys.md](../needlebase-uiux/journeys.md).

| Slice | Focus | Status |
|-------|-------|--------|
| 0 | Scaffold: sln, Clean Architecture, health, README | Done |
| 1 | Auth + Business tenancy (JWT, refresh, register business) | Done |
| 2 | Customers + measurements | Done (its code gaps G1, G4, G5 closed 20 Oct 2026: [code-gaps.md](code-gaps.md)) |
| 3 | Orders + production status (mobile primary) | Done (mobile data syncs via Slice 4) |
| 4 | Offline sync push/pull + DeletedItem + SyncChip | In progress: code + automated tests done 20 Oct 2026; Journey 2 not yet verified on devices (two-device sync, airplane mode) |
| 5 | Payments + balances + receipts (conflict-safe) | In progress: code + automated tests done 22 Oct 2026; Journey 1 steps 6–7 and Journey 4 not yet verified on a device |
| 6 | Web dashboard + production board | Planned |
| 7 | Staff invites + role-limited UI | Planned |
| 8 | Subscription/trial + polish | Planned |

---

## Slice 0 acceptance — Scaffold

- `NeedleBase.sln` with Domain, Application, Infrastructure, Api, Web, Mobile, Mobile.Data (+ test projects stubbed or present)
- Dependency rule enforced (no Web→SQL; no Mobile→Infrastructure/Api project refs)
- Api `/health/live` (and ready stub or DB-ready when SQL wired)
- README: how to restore/build/run Api + Web; secrets via user secrets / env — never committed
- Package baselines: NUlid, EF Core targets documented; no unused Mediator/Redis “for later”

## Slice 1 acceptance — Auth + Business

- Register business + owner; login; JWT + refresh tokens; `X-Auth-Token` → Authorization mapping for mobile
- Claims include `sub`, `business_id`, `role` (Owner for first user)
- All tenant queries prepared to filter `BusinessId` (global filter or explicit — document which)
- Mobile: secure token store; splash/session path; **no** `ClearTokens()` on transient offline/refresh failure
- Web: AuthApi + NeedleBaseApi HttpClients; cookie/session; fail if `Api:BaseUrl` missing
- ULID string IDs (NUlid) from the start
- Tests: auth happy path + unauthorized; BusinessId required on protected endpoints

## Slice 2 acceptance — Customers + measurements

- Domain: `Customer`, `MeasurementProfile` with BusinessId; Application DTOs (list vs detail)
- Api CRUD authorized + BusinessId scoped; paginated lists; `AsNoTracking` reads
- Mobile: Customers tab; add/edit; measurements with garment template; local SQLite save
- Toast/copy: “Saved on this device” when offline path exists (full sync may still be Slice 4)
- Web: Customers list/detail deferred to Slice 6 (oversight) — mobile-only for this slice
- Field limits consistent Domain + EF + DTOs + MAUI validation in the **same** slice
- Journey 1 steps 3–4 pass for customers/measurements

## Slice 3 acceptance — Orders + production

- Domain: `Order`, `OrderLine`; statuses `New → Cutting → Sewing → Fitting → Ready → Collected` (+ Cancelled); Overdue derived
- New Order wizard on mobile; measurement snapshot on lines; order ref ULID/readable as designed
- Status updates on order detail; role rules: Tailor limited when staff exists (full staff gate in Slice 7)
- Api order endpoints BusinessId scoped; list DTOs slim
- Mobile Orders tab + FAB New Order; compiled bindings; busy guards
- Journey 1 step 5 (order create) and status visible on detail

## Slice 4 acceptance — Offline sync (Journey 2)

- `SyncQueueItem`, `DeviceInfo` in Mobile.Data; auto-queue on SaveChanges unless `SuppressSyncQueue`
- `POST api/sync/push` + pull since `LastSync` + `DeletedItem` processing; BusinessId on all ops
- Pull apply uses `SuppressSyncQueue` + `finally` reset; batch size (~200); single orchestrator + semaphores
- Idempotent on client ULIDs — reopen + sync does **not** duplicate
- SyncChip + Sync details: Offline / Waiting to sync / Syncing / Synced / Failed
- Token honesty: transient refresh failure does not wipe queue or force login
- Journey 2 fully passes

**Slice 4 status (20 Oct 2026):** all items above built [code] except the device run of Journey 2.
Server: `Api/Controllers/SyncController.cs`, `Application/Sync/*`, `Domain/Entities/DeletedItem.cs`, migration `20261020131925_Sync`.
Mobile: `Mobile.Data/Data/SyncQueueItem.cs`, `SyncQueueWriter.cs`, `Mobile.Data/Sync/SyncEngine.cs`, migrator v4, `Mobile/Services/SyncService.cs`, `TokenRefreshHandler.cs`, `HttpSyncApi.cs`, Home SyncChip + `SyncDetailsPage`.
Tests: `Api.Tests/SyncEndpointTests`, `Mobile.Tests/SyncEngineTests`, `LocalSchemaMigratorTests`.
Edge-case audit fixes (20 Oct 2026): commit-ordered pull (txid + snapshot cap), tombstone retention/resurrection, parent-deleted/delete-refused conflicts, deferred children, Discard, re-sign-in without wipe, non-retryable 400/403, Retry-After; migration `20261020135519_SyncTxOrder`, migrator v5; tests `Api.Tests/SyncEdgeCaseApiTests`, `Mobile.Tests/SyncEdgeCaseTests`.

Second audit fixes (20 Oct 2026): adoption of pre-sync rows (ServerKnown, migrator v6), discard impact prompt, other-account sign-in prompt, shared decimal parser + amount rules, sync epoch (`SyncState`, recover resync), refresh grace window, per-user sync rate limit, StopAsync before wipe, accent-folded search (makes commit 97b1d85's "accent-insensitive" claim true), SQLite busy timeout + storage-full copy, order-line snapshots; migration `20261020153002_SyncEpochRefreshGrace`.

Third audit fixes (21 Oct 2026): lenient sync push, Android Back guard (`IBackAware`), foreground/periodic/pull-to-refresh sync, wipe on deactivation (`AccessRevokedService`), +233 phone normalisation + duplicate warning, server search keys, optional `baseModifiedUtc` → 409 `STALE_UPDATE`, refresh rate-limit policy + forwarded headers, 118 in cap, page cap, en-GH line totals, background epoch bump (503 `SYNC_MAINTENANCE`), per email+IP login throttle (no lockout), changed-only measurement checks; migration `20261021094529_CustomerSearchKeysPhone`, migrator v7. Device checks still open: Back during save, resume sync, duplicate prompt, deactivation wipe, v7 upgrade.
Device speed (22 Oct 2026): cached Orders/Customers lists, WAL, icon fix, Release profiled AOT — see [code-gaps](code-gaps.md) "Device speed" and G12. Slice 4 device checks added: Orders/Customers tabs during a real (reachable-API) sync and after a pull that changes rows; wipe on a WAL database leaves no `-wal`/`-shm`.
Deviations (conservative): pull is by opaque cursor (not a raw `LastSync` time); no per-business distributed lock (the skill marks it optional; push is idempotent instead). Mark Done after Journey 2 passes on two devices.

## Slice 5 acceptance — Payments (Journey 1 deposit + Journey 4)

- Domain: `Payment`; methods Cash/MoMo/Bank/Other; balances = order total − sum(payments)
- Record deposit and additional payments; receipt preview/share; offline = local record + pending sync
- **No silent overwrite** on payment conflicts — conflict payload + UI resolution
- Api + mobile sync rules aligned; payments never look provider-confirmed when only local
- Journey 1 step 6–7 and Journey 4 pass

**Slice 5 status (22 Oct 2026):** built [code] except the device run of the journeys (the phone was signed out).
Server: `Domain/Entities/Payment.cs`, `Domain/Enums/PaymentMethod.cs`, `Application/Payments/*` (`PaymentRules`, `PaymentService`), `Contracts/Payments/PaymentDtos.cs`, `Api/Controllers/PaymentsController.cs`, sync pull phase 4 (`SyncCursor.PhasePayments`, cursor stays v2), migration `20261022084825_Payments` (+ `trg_payments_sync_tx`).
Mobile: migrator v8 (`Payments`, FK cascade to Orders), `PaymentLocalService`, `PaymentsPage` (tab), `RecordPaymentPage`, `PaymentReceiptPage` (text share), Order detail payments section; SyncEngine payment push/pull/defer/prune/conflicts.
Tests: `Api.Tests/PaymentApiTests`, `Mobile.Tests/PaymentSyncTests`, `Domain.Tests/PaymentTests`, `Application.Tests/PaymentRulesTests`.
Decisions (defaults): payments are immutable — fix by **void** (one-way, first reason wins), no edit/delete/refunds; balance = total − non-voided payments; REST/device entry refuses overpay (`PAYMENT_EXCEEDS_BALANCE`) and Cancelled orders (`PAYMENT_ORDER_LOCKED`), Collected orders still take payments while a balance remains; sync push is lenient (offline money is real: overpay/cancelled stored, shown as "overpaid"); Owner + Manager only (reads too) — Tailor gets 403 `PAYMENT_FORBIDDEN` and the UI hides payments; order delete refused while payments exist (409 `ORDER_HAS_PAYMENTS` / sync DeleteRefused); same payment id with different content → `SYNC_PAYMENT_MISMATCH` conflict (Keep mine = keep both under a new id, Keep theirs = server copy); recording starts from Order detail.
Deferred: deposit step inside the order wizard (record from Order detail right after create); image/PDF receipt and print; Payments tab search/paging beyond 100 rows; per-row pending badges in lists (Slice 8).
Edge-case audit (22 Oct 2026): REST/sync payment writes lock the order row + a per-id advisory lock (no overpay race, no duplicate on concurrent same-id, delete-vs-record never 500s); order delete runs under the same order lock; sync pull skips payments for Tailors and resets their payments position (promotion re-pulls all); Tailor phone purges synced payment copies (queued ones kept) and resyncs; amount parser accepts "10,50"/"1,000"/"GH¢", refuses "1.000" and minus; receipt share omits the internal note; cancelled orders show "Order cancelled · GH₵ X recorded"; Payments tab cache keyed by role + "latest 100" note; Record payment honours the Android Back guard; Sync details has payment-specific conflict wording ("Restore order", "Keep both"/"Use theirs").
Second audit (22 Oct 2026): Tailor purge only removes server-confirmed copies; `PAYMENT_EXCEEDS_BALANCE` is 409 with the "Only GH₵ X is left" detail; void is serialised (first reason wins under concurrency); REST refuses payment dates more than 5 min ahead or before 2020, sync dates them "now"; sync stores unknown methods from newer apps as Other with "[label]" in the note; concurrent same-id pushes return PaymentMismatch (not Rejected); Order detail balance live-refreshes; method radio buttons have spoken names.
Device checks still open: record deposit offline → sync → second device sees balance; void on device A while B records; receipt share sheet; Tailor hides payments; v8 upgrade on an existing DB.

## Slice 6 acceptance — Web oversight (Journey 3)

- Web shell: sidebar IA from uiux; Dashboard + Production board + Orders
- Production board shows status columns/cards + assignee when present
- All data via Api HTTP; pagination/filter server-side; parallel independent fetches where useful
- Role-aware nav (hide Staff/Subscription until Slice 7/8 if not ready — do not fake)
- Journey 3 passes (mobile status/assign → visible on web)

## Slice 7 acceptance — Staff + roles (Journey 5)

- `Invitation`, `StaffMembership`; roles Owner / Manager / Tailor
- Invite accept flow; Tailor: no Staff/Subscription; Payments hidden by default; assigned orders only where applicable
- Policies: `RequireOwner`, `RequireManagerOrOwner`, `RequireStaff`
- Mobile + Web nav filtered by role
- Solo businesses still work with zero staff
- Journey 5 passes

## Slice 8 acceptance — Subscription + polish

- Trial/subscription surfaces per uiux (non-blocking for everyday work)
- Empty/loading/error/recovery on major flows; Sync details complete
- **Per-row pending-sync badge** (backlog, not built): Customers, Orders (and Payments once Slice 5 lands) rows show a small "Not synced yet" badge while the row has a queued `SyncQueueItem`, and "Needs attention" when its op is failed/conflicted; tap opens Sync details. List DTO gets a pending flag from the local queue (no server change); clears on ack/pull. Tests: list VM shows/clears the badge; no badge for synced rows.
- Indexes on BusinessId + hot filters; list page-size caps enforced
- Smoke tests for auth, BusinessId isolation, sync idempotency, payment conflict path
- No invented multi-currency or multi-business switching

---

## Slice notes (cross-cutting)

### Per-entity add checklist

Single source: [needlebase-domain](../needlebase-domain/SKILL.md#add-entity-checklist-single-source). Required tests: [testing.md](../needlebase-architecture/testing.md).

### Stock of “done”

Mark a slice **Done** in the table only when its acceptance list is met (or explicitly waived by the user in writing). Do not start the next slice without confirmation.

## Out of scope until explicitly requested

Multi-currency runtime, multi-business workspace switcher, full web offline parity, external payment-provider webhooks as source of truth, AI, Redis, Excel/PDF reporting suites, rewriting away from Clean Architecture / MVVM.
