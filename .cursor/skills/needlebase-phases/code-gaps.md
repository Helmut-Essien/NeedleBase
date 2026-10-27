# Code gaps backlog (skills = target, code = current)

The skills define the **target standard**. Each item here is (or was) a place where the code did not comply. When you find a new one, add it as `Gn` with files, required change and an acceptance test, and add an inline `> Code gap (to implement): … (Gn)` marker in the skill. Fix one item per change, following [engineering-review](../needlebase-architecture/engineering-review.md). When it's fixed, move it to **Done** with the date and remove the inline marker.

Feature work that isn't built yet (Orders, sync, payments, staff) is **not** listed here. That is the slice plan in [phases.md](phases.md).

## Open

None right now. The only remaining items are ops work, deferred below.

## Done

| # | Gap | Done | What changed | Tests |
|---|-----|------|--------------|-------|
| G1 | Tenancy filter failed open (security) | 20 Oct 2026 | `ApplicationDbContext` filters: `CurrentBusinessId != null && x.BusinessId == CurrentBusinessId`. Login/refresh membership lookups in `Identity/AuthService.cs` use `.IgnoreQueryFilters()` scoped by user id. The seeder and JWT validation already opted out. | `Api.Tests/TenancyIsolationTests` (null context → 0 rows; B can't GET/PUT/DELETE A's customer or measurement → 404) |
| G2 | Mobile SQLite used `EnsureCreated` | 20 Oct 2026 | `Mobile.Data/Data/LocalSchemaMigrator.cs`: forward-only SQL steps, version in `PRAGMA user_version`. Existing `EnsureCreated` DBs are baselined as v1 with no data change. `ILocalDatabaseService.EnsureCreatedAsync` was renamed `MigrateAsync` (startup + wipe). | `Mobile.Tests/LocalSchemaMigratorTests` (fresh = model, legacy baseline keeps rows, idempotent) |
| G4 | Use-case services in Infrastructure | 20 Oct 2026 | `CustomerService` and `MeasurementService` moved to `Application/Customers/` behind `Application/Abstractions/IApplicationDbContext`. Application references `Microsoft.EntityFrameworkCore` (abstractions only, no provider). Infrastructure DI registers `IApplicationDbContext → ApplicationDbContext`. | Existing Api tests unchanged and passing |
| G5 | ProblemDetails contract tests | 20 Oct 2026 | none (tests only) | `Api.Tests/ProblemDetailsTests`: 404 `CUSTOMER_NOT_FOUND`, 409 `CUSTOMER_CONFLICT`, 400 `VALIDATION_FAILED` + `errors`, 401 `UNAUTHORIZED` |
| G6 | Clients ignored ProblemDetails | 20 Oct 2026 | Shared `Application/Contracts/Errors/{ApiProblem,AuthErrorMessages}.cs`. Mobile `IAuthService.LastErrorMessage` and web `IApiAuthService.LastErrorMessage` are shown by the Login/Register VMs and pages, with the old copy as fallback. | `Application.Tests/AuthErrorMessagesTests` |
| G10 | Mobile Release Api URL hard-coded | 20 Oct 2026 | Release reads the MSBuild property `NeedleBaseReleaseApiBaseUrl` (assembly metadata, `Configuration/ApiEndpoint.cs`, https only). Debug is unchanged (`http://localhost:5036/` + `adb reverse`). **Default is the placeholder `https://api.needlebase-placeholder.invalid/`**, so you must set the real host at publish time. | `Mobile.Tests/ApiEndpointTests` |

### Slice 3 review fixes (20 Oct 2026)

Fixed from the post-Slice-3 review: customer delete with orders → 409 `CUSTOMER_HAS_ORDERS` (API + mobile guard); typed order errors (`DomainRuleException` codes → `OrderError` → `ApiProblems.ForOrder`); server-generated line ids; assignee must be an active member; server-computed total with mismatch rejection; no `ex.Message` in mobile order errors; Home stage colours from `OrderStatusPresentation` + distinct Overdue tokens; 8-char unique `DisplayRef` with collision retry; case-insensitive search; Accra "today"; XC0022 warnings; GroupBy counts; `SecondaryButton`; stray `undefined` files removed. Tests: `OrderEndpointTests`, `OrderLocalServiceTests`, `OrderSupportTests`, `Domain.Tests/OrderTests`.

Fixed 21 Oct 2026: customer and measurement services (server + device) no longer return English strings; typed `RecordResult<T>`/`RecordError` with stable codes, mapped by `ApiProblems.ForRecord`, `SyncService` and the mobile VMs by code. HTTP statuses and codes unchanged. Tests: `Api.Tests/TypedRecordErrorApiTests`, `Application.Tests/RecordResultTests`, `Mobile.Tests/Audit3Tests` (phone by code). Follow-up 21 Oct 2026: device `OrderLocalService` (`SaveAsync`, `UpdateStatusAsync`) also returns `RecordResult<OrderDetailDto>`; `OrderError` → `RecordError` via `Application/Orders/OrderErrorMapping` (`ORDER_NOT_FOUND`, `ORDER_CONFLICT`, `ORDER_LOCKED`, `ORDER_STATUS_INVALID`, `ASSIGNEE_INVALID`, `ORDER_TOTAL_MISMATCH`, same codes as `ApiProblems.ForOrder`); UI copy unchanged (`OrderErrorMessages`). Tests: `Application.Tests/OrderErrorMappingTests`, `Mobile.Tests/OrderLocalServiceTests`.

Closed (Slice 8): per-row pending-sync badges on the phone's Orders/Customers lists; tapping the badge opens Sync details.

## Deferred (ops, later)

These are not to be implemented now (there is deliberately no ops/deployment skill; keep the notes here).

### G3: No CI
- **Files:** there is no `.github/workflows/`.
- **Later:** a GitHub Actions workflow that runs restore, `dotnet format --verify-no-changes`, builds Api + Web, and runs all 4 test projects against a Postgres service.
- **Acceptance:** a PR shows a green check; a failing test turns it red.

### G7: Production config & publish hygiene
- **Files:** `.gitignore` (only lower-case `*.publishsettings`); no documented production env var list.
- **Later:** ignore `*.publishSettings`, `*.pubxml.user`, `*.keystore`; document the env vars; confirm Production never runs migrate/seed (today both are gated on Development).
- **Acceptance:** `git check-ignore` ignores a `.publishSettings` and a `.keystore`; the Api starts in Production with only env vars set.

### G8: Logging
- **Files:** `Debug.WriteLine` call sites in Mobile/Mobile.Data that print exception text (e.g. `Services/AppStartupService.cs`); no request logging scope in Api.
- **Later:** an Api logging scope with traceId/BusinessId/UserId (ids only); mobile `ILogger<T>` with no user data.
- **Acceptance:** an Api log line carries traceId + BusinessId and no names or phones.

### G9: Android release signing
- **Files:** `src/NeedleBase.Mobile/NeedleBase.Mobile.csproj` has no Release signing properties.
- **Later:** signing via MSBuild properties fed from env/CI secrets, with the keystore kept outside the repo.
- **Acceptance:** a Release publish with the secrets set produces a signed AAB; without them it fails clearly.

### G11: Production proxy / multi-instance settings (from third audit, 21 Oct 2026)
- **Files:** `src/NeedleBase.Api/Program.cs` (ForwardedHeaders), `Infrastructure/Identity/LoginThrottle.cs` (in memory), `ApplicationDbContext` epoch bump guard (per process).
- **Later:** set `ForwardedHeaders:KnownProxies`/`KnownNetworks` for the real proxy and the Web host; if the Api runs on more than one instance, move the login throttle to a shared store and the epoch-bump guard to a DB advisory lock.
- **Acceptance:** behind the proxy, two clients on different IPs get separate auth limits; with 2 instances a throttled email+IP stays throttled on both.

Device speed (22 Oct 2026): Orders and Customers tabs cache rows and reload only on change (`LocalDataVersion`, bumped by SaveChanges, non-query and commit interceptors, the migrator and wipe); business id cached in memory; Home warms the Orders query; lighter rows; picker null guard; status icon glyphs/font alias fixed; SQLite WAL without `Cache=Shared`, startup checkpoint, wipe removes side files; Release profiled AOT (device-checked). Rules: [lists-and-local-data.md](../needlebase-mobile/lists-and-local-data.md).

### G12: Device speed follow-ups
- **EF compiled model (deferred; trigger: the next EF Core upgrade):** `dbcontext optimize` + `UseModel` froze the app at the first migrate on the phone. Re-try with that upgrade only, adding a model drift test and a device start-up check.
- ~~List refresh after a background sync while the tab stays open~~ **Closed 22 Oct 2026:** visible Orders/Customers/Home refresh live (`LocalDataVersion.ChangedBySync` + `LiveListRefresh`).
- **Wipe while a sync holds a connection:** the deactivation path stops sync first; a direct wipe during a running sync is not guarded beyond pool clearing.
- **Acceptance:** compiled model starts on the phone with migrate time ≤ today.


## Slice 5 — Payments (22 Oct 2026)

- Open: deposit step in the order wizard (deferred; record from Order detail).
- Open: receipt is text share only (no image/PDF/print).
- Open: Payments tab shows the latest 100 rows, no search.
- Open: payment role check trusts the token role claim (same as other endpoints; Slice 7 revisits).
- Open: device verification of Journey 1 steps 6–7 and Journey 4 (phone signed out on 22 Oct).

### Slice 5 edge-case audit (22 Oct 2026)
- Closed: REST overpay race and concurrent same-id records (order row lock + advisory id lock); order delete racing a payment (500 → clean 409/404).
- Closed: payments pulled to Tailor phones; role change served from the Payments tab cache; "10,50" read as GH₵ 1,050; receipt leaking the internal note; cancelled orders showing "left"; wrong Sync-details wording for payment conflicts; Record payment ignoring the Back guard.
- Open (decision): Tailor phone keeps queued payments recorded while it was a manager; they fail with PAYMENT_FORBIDDEN in Sync details until an owner re-records or they are discarded.
- Open: old app builds (pre-Slice 5) show "Customer has orders" for a refused order delete (cannot fix in old builds).
- Open: the purge runs when a Tailor opens the Payments tab, not on the role refresh itself.

### Slice 5 second audit (22 Oct 2026)
- Closed: purge could delete a never-acknowledged payment; EXCEEDS_BALANCE returned 400 and lost its detail; racing voids could overwrite the first reason; future/ancient payment dates accepted; unknown sync method failed real money forever; same-id push race mapped to Rejected; Order detail balance stale after a pulled void; unnamed method radios.
- Open (decision): payments dated before the order are allowed (deposit taken before the order was entered).

## Slice 6 — Web oversight (22 Oct 2026)

- Nothing was assigned to Slice 6 here. Fixed on the way: `UnauthorizedRedirectHandler` redirected on 403 too, signing a Tailor out when a role-limited call (payments) was refused; now 401 only.
- Open: web pages don't auto-refresh; a phone change shows on the next load (Journey 3 = refresh). Web has no create/edit forms yet.

### Slice 6 edge-case audit (22 Oct 2026)

- Fixed: status PATCH race (row lock), web refresh races on parallel fetches, PATCH timeout replay, bare 503/504 not shown as offline, private pages cacheable, forwarded headers after HTTPS redirection, blank 400 on expired antiforgery, board "See all" dropping the mine filter, past-end paging copy, `assignedTo=me` with no user.
- Open: web role changes wait for the next token refresh; multi-business switching not supported; no `(BusinessId, AssignedToUserId)` index.

## Audit trail (22 Oct 2026)

- Done: `AuditEvents` table (append-only, tenant-scoped, DB guard trigger, 6 indexes), capture in `ApplicationDbContext.SaveChanges` (REST, web, sync push incl. restore/keep-mine, seeding), security events (login ok/failed/throttled/blocked, logout, token reuse, register), sync session rows, payments-view rows, `GET /api/audit`, web Activity page/History panel, 2-year purge job (`Audit:RetentionDays`).
- Open: no password-change endpoint yet → `auth.password_changed` reserved (Slice 7). Throttle is per process (several Api instances → up to one row each). Payments seen on phones via sync are not logged as views. Keep-theirs/Discard on the phone change nothing on the server, so nothing is logged. TRUNCATE/DROP by the schema owner bypass the guard (run prod with a non-owner role). Mobile history screen deferred. Token reuse not covered by an automated test (needs the 30 s grace to pass).

### Audit trail edge-case audit (22 Oct 2026)

- Fixed: a SaveChanges retried after a failure (order-ref race, raced creates, sync replays) logged the failed attempt too (stale rows now dropped before capture); an emoji at a summary/user-agent cut point could leave half a character and fail the user's save; failed and throttled sign-ins shared one counter; the throttle scanned all keys on every call once full and could grow without bound under a flood; `::ffff:` IPv4 addresses; empty sync pushes logged a row; new-row snapshots repeated customer/order ids; dates in diffs were raw ISO UTC (now Accra "22 Oct 2026, 14:05"); raw field names (now "due date", "assignee"…); Managers offered a "Sign-in" filter that always returned nothing; no "Business details" filter; a former member's filter showed as "Everyone"; retention days unbounded above (now 30–36,500).
- Fixed (test): `CursorAheadOfCap_SameEpoch` counted epochs before letting a pull settle a re-stamp that had to be retried; now waits for the pull, still asserts exactly one bump.
- Open: pull session rows carry no item counts (sessions span many pages; one row per phone per hour); `search` is an unindexed ILIKE over one business's summaries and the total count scans the filtered rows (fine to ~10⁵ rows per business; add pg_trgm / keyset paging if it grows).

## Order edit + measurement snapshot (23 Oct 2026, before Slice 7)

- Done: `OrderLine.MeasurementTakenAtUtc` (Api migration `OrderLineMeasurementTakenAt`, phone schema v9, sync field; old clients omit it = null). Shown read-only on phone and web order detail with a customer link; old lines say "date not recorded"; no backfill.
- Done: Owner/Manager order edit (due date, notes, lines incl. price) on phone (`EditOrderPage`, offline + queued) and web (`/Orders/Edit/{id}`), one rule set in `OrderEditor` (API, sync push, phone). Existing lines keep garment + snapshot; new lines snapshot the latest profile for that garment. Refused: Tailors (`ORDER_EDIT_FORBIDDEN`, 403), Collected/Cancelled, total below paid when lowering (`ORDER_TOTAL_BELOW_PAID`, 409), stale. Audit lists added/removed items and "{Garment} quantity/unit price" changes (notes hidden).
- Changed: Tailors can no longer edit the order header via the Api (no screen used it).
- Changed: sync create/edit keeps the device's line ids (valid ULID, not used by another order) so replays and later edits match lines; pulls merge local lines in place (update kept ids, insert new, delete gone) when ids or content differ.
- Open: Tailors still see line prices and the order total on order detail (pre-existing; decide whether to hide). Web adds one garment per save. Edit doesn't change assignee or fitting date. Screenshots not taken (no seeded data allowed, no device connected).

### Review (26 Oct 2026)
- Done (speed): edit save (API, sync, phone) reads profiles only for new lines that need them (`OrderEditor.ProfileNeeds`) and paid (non-voided) only when lines change; sync push skips the foreign-line-id query for ids the order already has; phone pull merges lines in place (no delete-all/reinsert); phone edit screen parses qty/price once per keystroke, ignores notes typing and recalculates the total once on load (50 lines: 51 → 1 recalculation, load ~11.7 → ~9.9 ms, 200 price keystrokes ~5.1 → ~2.2 ms, in-memory SQLite); order detail keeps garment cards when lines are unchanged (back from Edit). Api edit on small orders unchanged (~8 ms median, dev Postgres). Indexes already cover the new queries (no migration).
- Done (edge cases): audit row for a large edit keeps the first 20 item details plus "more item changes" count; phone edit shows "locked" (not "stale") when the order became Collected/Cancelled while open; phone edit errors are announced to screen readers; web new-garment quantity has a max.
- Open: roles are fixed at join (no role-change feature); the role comes from the access token. When role changes are added, rotate the security stamp so tokens re-check. Order detail/edit use BindableLayout inside a ScrollView (bounded at 50 lines; CollectionView would lose virtualization inside the ScrollView). Tailor price visibility still undecided.

## Slice 7 — Staff + roles (26 Oct 2026)

- Done: invitations (code shared by hand, hash stored, 7 days, one use under a row lock), accept on web + phone, Owner-only role change / turn access off/on with security-stamp rotation, change password (other devices signed out), Tailor status rule (`ORDER_NOT_ASSIGNED`), Payments tab hidden for Tailors, More page replaces the placeholder.
- Review fixes: turned-off staff signing in got "isn't linked to a business" → now `ACCOUNT_DEACTIVATED` copy; web order detail showed the status form to Tailors on others' orders (Api refused) → hidden; phone change-password retries once after an expired access token and holds the refresh lock (no refresh racing the token wipe); phone stack-clear on sign-out no longer aborts when the Payments tab is hidden; invite/staff error copy shared (`StaffErrorMessages.ForProblem`) by web and phone.
- Done (Slice 7b): Gmail SMTP email (`Infrastructure/Email`: `EmailOptions` section `Email`, `SmtpTransport` MailKit STARTTLS 587, `EmailQueue` + `EmailSendingService` 3 tries 5 s/30 s, per-try timeout); invite emails; Forgot/Reset password (`api/auth/options|forgot-password|reset-password`, table `PasswordResetCodes`); `POST api/staff/{id}/reassign-orders` (Owner); shared `PasswordPolicy`.
- Open: owner transfer (by choice, not yet); invited emails unverified; Tailors see order totals (by choice); device + browser run of Journey 5 and of the reset/reassign flows.
- Open (email): the send queue is in memory — mail queued at a restart is lost (user asks again; invite code is still shown on screen). Gmail caps ~500 messages/day per account (lower for new accounts); a real provider (or Google Workspace) is needed past that. One instance assumed (no shared queue). No automated test of the 5 s/30 s retry timing (would add ~35 s).
- Open (reassign): capped at 2,000 orders per call (press again for the rest); a tailor's offline edit to a moved order conflicts under the normal sync rules.

### Slice 7 edge-case review (26 Oct 2026)
- Fixed: reset throttles are per (address, IP), not IP-wide, so people sharing a carrier IP (CGNAT) don't lock each other out; per-account caps (3 codes/hour, 5 tries/code) still bound guessing and mail. Trade-off: one IP can request resets for many different addresses (each capped at 3/hour; Gmail ~500/day).
- Fixed: email worker no longer blocks the queue on a failing message (retries scheduled in the background: 5 s, 30 s); unsent count logged on shutdown; SMTP disconnect error after a successful send no longer causes a duplicate retry.
- Fixed: names/business name flattened to one line (no CR/LF/control chars) in subject and To; invite email must be one plain address; invite name rejects control characters.
- Fixed: reassign locks the target's membership row inside the transaction (a concurrent "turn off" can't receive orders); staff overview ordered before Take(500).
- Fixed: web "Forgot password?" check serves the last known answer at once and refreshes in the background (one refresh at a time); a blip no longer hides the link.
- Open: tailor creating an order offline assigned to someone else with a non-New status — order is created, status change rejected (ORDER_NOT_ASSIGNED), op stays failed; rare. Forgot password still emails a user whose only membership is turned off (they can reset but not sign in). Order has no concurrency token; concurrent web edits during reassign are last-write-wins on AssignedTo (phones are protected by the sync staleness check).

### Slice 7 review, second pass (26 Oct 2026)
Fixed:
- Forgot password: an address with no active membership gets no email and no audit row; the reply is the same neutral 202.
- Reset requests: a cap of 60 per hour per IP across all addresses (`ResetRequestIpCap`, in memory, bounded). This is on top of the per-(address, IP) throttle and the per-minute "auth" rate limit. It is set high enough for shared CGNAT mobile IPs.
- Sync push: when a tailor creates an order offline with a status past New and the order is assigned to someone else, the order is now saved as New. The push returns Conflict with ORDER_NOT_ASSIGNED and the server copy, so the item is no longer stuck as failed. The mobile app shows a clear text for this. Other status errors on create still roll back.
- Reassign: the source's open orders are locked FOR UPDATE before they are moved, so a concurrent assignee edit can't be silently overwritten. The web order edit and status changes already had optimistic concurrency through BaseModifiedUtc and a stale message.
- Mobile: `GetCachedUserAsync` is memoized and cleared on sign-in and sign-out. This saves 7 Keystore reads per navigation.
- Mobile accessibility: status and error banners are announced to TalkBack; the old fixed "Error"/"Status" description hid the message. Web password hints are linked to their fields with aria-describedby.
- Tests: SyncEndpoint, Payment and AuditTrail API tests share the non-parallel `SyncEpoch` collection, so a background epoch bump can't break another class's paging. This was a flaky failure before this pass.

Query counts (measured by QueryCountTests):

| Path | Queries |
|---|---|
| auth-only request | 2 |
| staff list, 3 members | 4 |
| staff list, 8 members | 4 |
| accept invite | 12 |
| change password | 12 |
| forgot password | 7 |
| reset password | 9 |
| sync push, tailor status change | 9 |
| reassign, 3 orders | 9 |
| reassign, 12 orders | 9 |

No N+1: the staff list and reassign counts stay the same as size grows.

Open:
- The API test run takes about 24 s, up from about 12 s, because of the serial sync collection.
- A turned-off account gets no reset email at all; the owner has to turn it back on first.
- The IP cap is per process and is lost on restart.

## Slice 8 — WhatsApp + polish (26 Oct 2026)

- Built: WhatsApp tap-to-send (phone + web), automatic template messages (outbox + sender + webhook), settings/log page, per-customer consent, per-row pending-sync badge on phone lists. Tests: `Api.Tests/StaffApiTests.WhatsApp.cs`, `Api.Tests/WhatsAppLinksTests.cs`, `Web.Tests/WhatsAppPageTests.cs`, `Mobile.Tests/SyncBadgeTests.cs`.
- Added (26 Oct): phone consent (offline, schema v10 `WhatsAppConsents`, pushed after sync via `SyncService.AfterRun` → `WhatsAppConsentSync.PushAsync`; Api `PUT .../consent` takes `ChangedAtUtc`, newer server change wins, future times clamped), phone WhatsApp settings + log (`WhatsAppSettingsPage`/`ViewModel`), badge tap → Sync details, Sync details attention-first + load error, Home load error. Tests: `Mobile.Tests/WhatsAppMobileTests.cs` (9), `Mobile.Tests/SyncDetailsOrderTests.cs` (3), Api STOP-vs-late-opt-in + clock clamp in `StaffApiTests.WhatsApp.cs`.
- Gaps: trial/subscription **on hold (user decision)**; webhook host **on hold** (placeholder in `docs/whatsapp-setup.md`); an offline consent change is audited when it reaches the server (actor = the person who made it, time = the device time it was chosen, which is trusted unless in the future, so a phone clock far behind loses to newer changes); consent for a customer that was never synced waits until the customer syncs; the phone shows consent from its cache until the background refresh (one GET per customer open, online only); phone WhatsApp settings are hidden from Tailors (the Api refuses them anyway); no diagnostics screen behind Sync details; tap-to-send "opened" isn't recorded when the phone is offline; one platform WhatsApp number for all shops (a STOP applies to every shop with that number); the sender runs inside the Api process (a restart delays, never loses, pending rows); the web order page loads consent after the order (one extra round trip for Owner/Manager); no real Meta call has been made (needs credentials).

### Slice 8 review (2026-10-26)

Fixed:
- Old STOP replays undoing a newer opt-in: STOP now uses Meta's message time.
- A slow phone clock rejecting a fresh change: corrected with `DeviceNowUtc`.
- Hourly limit counting delivery receipts: now counts the new `SentAtUtc` column (migration `WhatsAppSentAt`, filtered index).
- A message sent at shutdown but not saved, so sent twice: the save after sending can't be cancelled.
- Emoji split by text cuts: `TextCut`.
- Ghana 03X landlines treated as WhatsApp numbers.
- On the phone:
  - a choice made during a push was lost;
  - opt-in changes waiting to send weren't counted in the sync badge;
  - a quick double tap asked twice.

Open:
- Meta has no idempotency key, so a send that crashes after Meta accepts it may be sent again after 10 minutes.
- The audit row's own time is the server's receive time; the phone time appears in the summary text.

### Measurement decimals (2026-10-27)

Fixed:
- Measurements like 4.125 or 37.250 (eighths of an inch) were refused as "must be a number", because the price rule that blocks "1.000" also applied.
  - Measurements now use `DecimalInput.TryParseMeasurement` (up to 4 decimals, "." or ",").
  - Prices and payments keep the strict rule.
- Saved values reopened with `ToString("0.##")` (4.125 appeared as 4.13 and could be saved back rounded). They now use `DecimalInput.Format`: exact, with the same text in every phone language.
- Switching between cm and inches rounds to 3 decimals instead of 2.
- The error now names the field and the text it couldn't read.
- The server (`AmountRules.CheckMeasurement`) refuses more than 4 decimals.
  - Phones already send at most that many.
  - Older phones only sent up to 2.

Open:
- Switching cm to inches and back more than once can drift in the 3rd decimal.
- A value saved before this fix with more than 4 decimals (the old parser allowed it) is refused again on a full re-check or sync of that record.
