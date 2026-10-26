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
| 6 | Web dashboard + production board | In progress: code + automated tests done 22 Oct 2026; Journey 3 (phone change → web) not yet verified with a signed-in phone and browser |
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

**Slice 6 status (22 Oct 2026):** built [code]; Journey 3 not yet run end to end in a browser with a signed-in phone.
Api: `GET api/dashboard/summary` (`Application/Dashboard/DashboardService.cs`, `Contracts/Dashboard`), `GET api/staff/names` (active members of the business: user id, name, role; no email/phone; max 500), `GET api/orders` gains `sort=due` (due date, no-date last), `status=open` (not Collected/Cancelled) and `assignedTo=me|<userId>`; `OrderListItemDto.AssignedToUserId`. No migration.
Web: sidebar shell (`_Layout`, `_NavLinks`; collapses to a top menu below 768px), Dashboard (`Pages/Index`), Production board (`Pages/Production`), Orders list + detail with status change (`Pages/Orders`), Customers list, Payments list (Owner/Manager). Typed client `Services/NeedleBaseApi` + `ApiResult` (ok / ProblemDetails / offline); `AppPageModel` maps 401 → login, 403/other → page message.
Tests: `Web.Tests` (client, page models, role gating, error mapping, 401 handler, rendered pages against a fake Api), `Api.Tests/DashboardApiTests`.
Decisions (defaults): web is read-mostly — the only write is order status change (PATCH with `baseModifiedUtc`, STALE_UPDATE shows the fresh order); creating/editing customers, measurements, orders, assignment and recording payments stay in the app for now; board moves go through Order detail (no drag and drop); each board column shows the 30 earliest-due cards with a "See all" link; Tailors' board starts on "Assigned to me" (toggle to everyone); money (dashboard totals, payments, order balance) shown only when the cookie role is Owner/Manager **and** the Api returns it; outstanding = sum of positive balances on non-cancelled orders; "received" uses Accra calendar days; Staff, Reports, Settings, Subscription and Measurements are not in the nav until built; a 403 from the Api no longer signs the user out.
Edge-case audit (22 Oct 2026): order status PATCH now locks the order row (two PATCHes with the same `baseModifiedUtc` can't both win); a STALE reply when the order already has the requested status shows "Status is X" (double submit); web token refresh no longer re-reads the cookie per call (parallel page fetches raced on HttpContext and could restore the pre-refresh token), refresh sign-out happens inside the per-user lock, the cached identity is cloned before claims change; only connection-refused calls are retried (a timed-out PATCH is never replayed; no 3 × 30 s waits); bare 502/503/504 show the offline state; signed-in pages send `Cache-Control: no-store` (static files keep caching); forwarded headers run first (no HTTPS redirect loop behind a proxy); antiforgery/404 errors show a friendly Error page; Orders keeps `assigned=me` from the board's "See all"; lists show "past the end" instead of "no orders" for a stale page link; `assignedTo=me` without a user id matches nothing; dashboard outstanding is one query; keyboard focus rings on nav/cards.
Known limits (decisions): a role change takes effect on the web at the next token refresh (the Api's JWT role is authoritative until then); one business per user (no switching); Tailors see order totals (not payments/balances); staff names include each member's role; no extra index for `assignedTo` (Status/DueDate indexes cover current tenant sizes).
Deferred: web create/edit forms (amount/phone/measurement validation will reuse the shared parsers when they land); auto-refresh (the page shows data as of load — refresh to see phone changes); customer detail page.

## Slice 7 acceptance — Staff + roles (Journey 5)

- `Invitation`, `StaffMembership`; roles Owner / Manager / Tailor
- Invite accept flow; Tailor: no Staff/Subscription; Payments hidden by default; assigned orders only where applicable
- Policies: `RequireOwner`, `RequireManagerOrOwner`, `RequireStaff`
- Mobile + Web nav filtered by role
- Solo businesses still work with zero staff
- Journey 5 passes

**Slice 7 status (26 Oct 2026):** built [code]; Journey 5 not yet run on a device (none connected) or in a browser.
Server: `Domain/Entities/Invitation.cs` (7-day code, Pending/Accepted/Revoked/Expired), `StaffMembership.ChangeRole/Deactivate/Reactivate` (never the Owner), `Infrastructure/Staff/StaffService.cs`, `AuthService` accept-invite (row lock, one use) + change-password, `Api/Controllers/StaffController.cs` (`GET api/staff` Manager/Owner; invite, revoke, role, deactivate, reactivate Owner only), `POST api/auth/invite-preview|accept-invite` (anonymous, throttled per IP) and `change-password` (RequireStaff); migration `20261026084543_Invitations`. Codes in `StaffErrorMessages` (`INVITE_*`, `STAFF_*`, `PASSWORD_*`); `ORDER_NOT_ASSIGNED` (403) when a Tailor moves an order assigned to someone else (REST + sync).
Web: `Pages/Staff` (Owner manages, Manager views), `Pages/Account/AcceptInvite` (+ link on sign-in), `Pages/Profile/Password` (link next to Sign out); Staff nav for Owner/Manager; Tailor status control only on own/unassigned orders.
Mobile: `MorePage` (Staff for Owner/Manager, Change password, About), `StaffPage` (online only; share code via the share sheet), `AcceptInvitePage` (from sign-in), `ChangePasswordPage`; Payments tab hidden for Tailors; order status picker hidden on others' orders for Tailors.
Tests: `Api.Tests/StaffApiTests`, `Web.Tests/StaffPageTests`, `Mobile.Tests/StaffMobileTests`, `Domain.Tests/InvitationTests`, `Application.Tests/StaffRulesTests`.
Decisions (defaults): no email/SMS provider, so the owner shares the code by hand (WhatsApp/SMS/in person) and it is shown once (only a SHA-256 hash is stored); one account = one business, so an email that already has an account can't be invited; re-inviting an email cancels its old code; max 20 open invites; only the Owner changes staff; Owner can't be changed/deactivated and nobody changes themselves; role change/deactivation rotate the user's security stamp (old tokens fail, refresh gives the new role or `NO_MEMBERSHIP` → the phone wipes its data); sign-in for a turned-off member says `ACCOUNT_DEACTIVATED`; password change signs out every other device (refresh tokens deleted) and keeps this one; Tailors still receive all orders in sync (needed offline) but can only move their own or unassigned ones.
Slice 7b (26 Oct 2026): Gmail SMTP email (MailKit, config/user-secrets only; off → codes shared by hand + startup warning); invite codes also emailed; Forgot/Reset password on web + mobile (shown only when email is configured; 8-digit hashed single-use code, 30 min, 5 tries, 3/hour, IP throttle, signs out every device, audited without secrets); Owner reassigns a turned-off member's open orders (web + mobile, offered right after turning access off and from the staff list; bulk, transactional, audited, sync-safe); one shared password rule (`Application/Common/PasswordPolicy`) drives the web + mobile hints. Deferred: owner transfer; invited users' email isn't verified (the owner typed it); Staff nav shows for a solo Owner (IA says "hidden until someone is invited") so the invite entry point is reachable.

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

### Audit trail (cross-cutting, 22 Oct 2026)
Not owned by one slice (only “Recent activity” was in the IA). Built after Slice 6: `AuditEvents` (append-only), Api `GET /api/audit`, web **Activity** page + order History panel + customer History link. Slice 7 must keep emitting `staff.*` (captured automatically from `StaffMembership` changes) and emit `auth.password_changed` from the password-change endpoint it adds. Mobile read-only history: deferred.

## Out of scope until explicitly requested

Multi-currency runtime, multi-business workspace switcher, full web offline parity, external payment-provider webhooks as source of truth, AI, Redis, Excel/PDF reporting suites, rewriting away from Clean Architecture / MVVM.
