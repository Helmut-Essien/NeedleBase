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
| Orders (slice 3) | typed codes `ORDER_LOCKED`/`ORDER_STATUS_INVALID`/`ASSIGNEE_INVALID`/`ORDER_TOTAL_MISMATCH`; server line ids; 8-char ref; case-insensitive search; `CUSTOMER_HAS_ORDERS` | ✓ `OrderEndpointTests`, `Domain.Tests/OrderTests` |
| Mobile orders | friendly errors (no `ex.Message`), local delete guard, totals, search | ✓ `OrderLocalServiceTests` |
| Customer search | name word prefix/case, phone variants (`024`, `+23324`, spaces), limit + ranking, recent, keys refresh on edit, debouncer cancels stale; v3 backfill | ✓ `Mobile.Tests/CustomerSearchTests`, `Application.Tests/CustomerSearchKeysTests`, `LocalSchemaMigratorTests` |
| Order helpers | error copy, domain-code mapping, Accra "today", ref normalising | ✓ `Application.Tests/OrderSupportTests` |
| Every new entity (slice 3+) | tenancy isolation + idempotent create + ProblemDetails 404/409 | required |
| Sync (slice 4) | idempotent push, conflicts, tenancy, tombstones, cursor paging (`Api.Tests/SyncEndpointTests`); queue/coalesce, push ack, fresh-device pull without duplicates, transient keeps queue, tombstone, conflict resolve, HTTP failure mapping (`Mobile.Tests/SyncEngineTests`); migrator v4 | built; two-device + airplane-mode run is manual |
| Sync edge cases | tombstone resurrection, first pull without tombstones, open-transaction row not skipped, order create+status atomic, foreign ids, delete-refused / parent-deleted conflicts, expired/v1 cursor, tombstone purge (`Api.Tests/SyncEdgeCaseApiTests`); order/measurement round trip, stale tombstone, deferred children, Discard, resync prune, parents-first, page+cursor atomicity, 400/403/Retry-After, chip states, re-sign-in wipe rules, single refresh (`Mobile.Tests/SyncEdgeCaseTests`) | built |
| Payments (slice 5) | conflicting edits → conflict payload, never silent overwrite | planned |

Code gaps: [code-gaps.md](../needlebase-phases/code-gaps.md).

## Second-audit regression tests

- `Application.Tests/AuditFixRulesTests` (decimal parser, amount rules, folding, highlight ranges, line snapshots).
- `Api.Tests/AuditFixSyncApiTests` (grace window, epoch bump + no loop + no bump storm, rate limit) — in the `SyncEndpointTests` class so epoch bumps never run beside other pulls; `AuditFixOrderApiTests` (REST amount rules, snapshot).
- `Mobile.Tests/SyncAuditFixTests` (adoption, recover resync, 429 inline wait, StopAsync, discard impact, other-account prompt, storage, due-date warning); migrator v5→v6 upgrade and 20k-row key backfill in `LocalSchemaMigratorTests`.
## Third-audit regression tests

- `Domain.Tests/PhoneNumbersTests`; `Application.Tests/Audit3RulesTests` (lenient lines, changed-only measurements, 118 in, page cap, stale check).
- `Api.Tests/Audit3OrderApiTests` (phone 400 + matches, `STALE_UPDATE` on customer/order/status, server search keys, huge page, `LOGIN_THROTTLED`), `LoginThrottleTests`, `Audit3SyncApiTests` (lenient push, phone replay, measurement stale, backfill keeps DateModifiedUtc) — sync ones in `SyncEndpointTests`.
- `Mobile.Tests/Audit3Tests` (phones, migrator v7, over-limit edit, revoked flag + wipe, AccessRevoked event, foreground/pull-to-refresh sync, en-GH totals, `IBackAware`).
- Epoch bumps run in the background: pulls get 503 `SYNC_MAINTENANCE` until done; tests use `ApplicationDbContext.WaitForEpochBumpAsync()` and retry on 503.

## Typed error regression tests

- `Application.Tests/RecordResultTests` (distinct codes, copy, deconstruct, null replay is not success), `OrderErrorMappingTests` (every `OrderError` → same code as `ApiProblems.ForOrder`).
- `Api.Tests/TypedRecordErrorApiTests` (customer/measurement 404/409/400 statuses + codes unchanged, `MEASUREMENT_SAVE_FAILED`, measurement `STALE_UPDATE`).
- `Mobile.Tests/Audit3Tests` (phone error by `RecordError.PhoneInvalid`), `OrderLocalServiceTests` (order `RecordError` + codes, copy unchanged).

- Epoch tests bump the shared dev database's epoch: dev devices do one full resync afterwards.

## Rules

- A bug fix ships with a test that failed before the fix.
- Don't assert on UI copy in Api tests; assert `status` + `code`.
- Mobile ViewModel tests construct VMs with real/fake services (see `HomeViewModelTests`); no MAUI runtime required.
- If tests can't run (no Postgres), say so explicitly in the report. Never claim they passed.
