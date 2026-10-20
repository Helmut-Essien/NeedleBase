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

## Deferred (ops, later)

These are not to be implemented now. Ops guidance will get its own skill later.

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
