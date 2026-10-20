# NeedleBase slices

Deliver **one slice at a time**. Confirm with the user before starting the next. Every slice must follow [engineering-review](../needlebase-architecture/engineering-review.md) and [code-quality-performance](../needlebase-architecture/code-quality-performance.md). Journey acceptance: [journeys.md](../needlebase-uiux/journeys.md).

| Slice | Focus | Status |
|-------|-------|--------|
| 0 | Scaffold: sln, Clean Architecture, health, README | Done |
| 1 | Auth + Business tenancy (JWT, refresh, register business) | Done |
| 2 | Customers + measurements | Done (its code gaps G1, G4, G5 closed 20 Oct 2026: [code-gaps.md](code-gaps.md)) |
| 3 | Orders + production status (mobile primary) | Planned |
| 4 | Offline sync push/pull + DeletedItem + SyncChip | Planned |
| 5 | Payments + balances + receipts (conflict-safe) | Planned |
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

## Slice 5 acceptance — Payments (Journey 1 deposit + Journey 4)

- Domain: `Payment`; methods Cash/MoMo/Bank/Other; balances = order total − sum(payments)
- Record deposit and additional payments; receipt preview/share; offline = local record + pending sync
- **No silent overwrite** on payment conflicts — conflict payload + UI resolution
- Api + mobile sync rules aligned; payments never look provider-confirmed when only local
- Journey 1 step 6–7 and Journey 4 pass

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
