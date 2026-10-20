# Testing (NeedleBase)

Run everything: `dotnet test` from the repo root (solution). Api tests need PostgreSQL on **localhost:5435**: `docker compose up -d` (from `docker-compose.yml`) or set `ConnectionStrings__DefaultConnection`.

## Projects (real)

| Project | Target | What's there today |
|---------|--------|--------------------|
| `tests/NeedleBase.Domain.Tests` | net9.0 | `BusinessTests`, `CustomerTests`, `EntityTests`: invariants |
| `tests/NeedleBase.Application.Tests` | net9.0 | `IdGeneratorTests` |
| `tests/NeedleBase.Api.Tests` | net9.0 | `NeedleBaseApiFactory` + `AuthEndpointTests`, `CustomerEndpointTests`, `HealthEndpointTests` |
| `tests/NeedleBase.Mobile.Tests` | net9.0 | `AuthServiceTokenHonestyTests`, `CustomerLocalServiceTests`, `HomeViewModelTests`, `PasswordToggleViewModelTests` |

Packages: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, coverlet.collector, Microsoft.AspNetCore.Mvc.Testing, Microsoft.EntityFrameworkCore.Sqlite. **No mocking library is used.** Prefer real implementations, EF Sqlite in-memory, and small hand-written fakes. Add NSubstitute only if a fake becomes unwieldy, and record that decision here.

## Api integration pattern

`NeedleBaseApiFactory : WebApplicationFactory<Program>`:
- forces a test JWT key (env var **and** `UseSetting` so TokenService and JwtBearer agree);
- uses the Postgres connection string from env or the 5435 default;
- sets `Seed:DevelopmentData=false`.

Tests take `IClassFixture<NeedleBaseApiFactory>`, register a fresh business with a `Guid` suffix (no shared state), then call endpoints with the returned bearer token. `Program` is exposed via `public partial class Program;`.

## Required tests (definition of done per slice)

| Concern | Required test | Exists? |
|---------|---------------|---------|
| Auth | happy path register → login → `/api/me`; unauthenticated → 401 | ✓ `AuthEndpointTests` |
| Tenancy isolation | business B can't list or get business A's records (404) | ✓ `CustomerAndMeasurement_Crud_IsBusinessScoped` |
| Tenancy fail-closed | DbContext with null `CurrentBusinessId` returns 0 rows; B can't update/delete A's customer or measurement | ✓ `TenancyIsolationTests` |
| Idempotency | same client ULID twice → one record | ✓ `CreateCustomer_WithClientUlid_IsIdempotent` |
| ProblemDetails | each error path returns `application/problem+json` + `code`, no `message` | ✓ `ProblemDetailsTests` (404/409/400/401) + login |
| Client error copy | ProblemDetails `code` → friendly auth copy, generic fallback | ✓ `AuthErrorMessagesTests` (Application.Tests) |
| Local schema (mobile) | fresh DB matches model; legacy `EnsureCreated` DB baselined without data loss | ✓ `LocalSchemaMigratorTests` |
| Release API URL (mobile) | placeholder by default; https only | ✓ `ApiEndpointTests` |
| Token honesty (mobile) | transient refresh failure keeps tokens | ✓ `AuthServiceTokenHonestyTests` |
| Offline local save | save → reload → present | ✓ `CustomerLocalServiceTests` |
| Every new entity (slice 3+) | tenancy isolation + idempotent create + ProblemDetails 404/409 | required |
| Sync (slice 4) | offline create → reopen → sync once, no duplicate | planned |
| Payments (slice 5) | conflicting edits → conflict payload, never silent overwrite | planned |

Code gaps: [code-gaps.md](../needlebase-phases/code-gaps.md).

## Rules

- A bug fix ships with a test that failed before the fix.
- Don't assert on UI copy in Api tests; assert `status` + `code`.
- Mobile ViewModel tests construct VMs with real/fake services (see `HomeViewModelTests`); no MAUI runtime required.
- If tests can't run (no Postgres), say so explicitly in the report. Never claim they passed.
