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

Open (backlog, Slice 8): per-row pending-sync badges on lists; today only the sync chip + Sync details show pending counts.

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
