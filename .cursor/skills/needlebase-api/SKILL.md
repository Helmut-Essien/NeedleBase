---
name: needlebase-api
description: >-
  NeedleBase ASP.NET Core Api + Infrastructure: controllers, JWT/refresh auth
  and policies, fail-closed Business tenancy filters, EF Core PostgreSQL and
  migrations, ProblemDetails errors with stable codes, health/ping, and
  (planned) sync push/pull with DeletedItem. Use when editing Api controllers,
  middleware, Program.cs, Infrastructure persistence/identity, EF migrations,
  or API error handling.
---

# NeedleBase API & Infrastructure

Composition: **Api** host + **Infrastructure** implementations of **Application** interfaces. Domain stays persistence-ignorant.

**Built:** auth (register-business, login, refresh, logout), `/api/me`, Customers + Measurements CRUD, Orders CRUD + status patch, health, ping, ProblemDetails. **Planned:** sync push/pull + `DeletedItem` (S4), Payments (S5), Staff/Invitations (S7), Subscription (S8).

## Hard rules

1. **ULID string IDs** (NUlid) — never int/Guid PKs for domain entities.  
2. **BusinessId filter, fail closed** — every tenant entity gets a global filter `x.BusinessId == CurrentBusinessId` set by `BusinessTenancyMiddleware` from the `business_id` claim. **Null business id → no rows.** Login/refresh/membership checks use `.IgnoreQueryFilters()` with explicit `UserId`/`BusinessId` predicates. Every tenant endpoint has a cross-tenant isolation test.  
   [code] Filters are `CurrentBusinessId != null && x.BusinessId == CurrentBusinessId`; `Identity/AuthService.cs` login/refresh membership lookups use `.IgnoreQueryFilters()` scoped by user id. Tests: `TenancyIsolationTests`.  
3. **DeletedItem (planned S4)** — `SaveChanges` will track deletes for mobile pull, with BusinessId. Not in code yet; don't reference it until S4.  
4. **Sync push (planned S4)** — authorize user; scope to business; optional distributed lock per user/business (409 if busy). Idempotent on client entity ULIDs.  
5. **Payment conflicts** — do not silently overwrite; return conflict payload for mobile UI.  
6. **Auth** — JWT Bearer + refresh tokens; map `X-Auth-Token` → `Authorization` for mobile. Do not log tokens in Production.  
7. **Health** — `/health/live`, `/health/ready` (DB), mobile ping `GET /api/test/ping` (`DiagnosticsController`) — all anonymous.  
8. **Migrations** — In **Development only**, `Program.cs` listens first, then runs `MigrateAsync()` + `DevelopmentDataSeeder` (logs and continues if Postgres is down). Production never auto-migrates: apply a reviewed `dotnet ef migrations script --idempotent` after a DB backup. Never edit an applied migration.  
9. **Email links (planned)** — use `FrontendBaseUrl` config; never hardcode Web hostnames.  
10. **Thin endpoints** — controllers call Application use-case services and return DTOs or `ApiProblems.*`.
   [code] `Application/Customers/` (via `IApplicationDbContext`).  
11. **Speed & EF hygiene** — `AsNoTracking()` + DTO projection for reads; index `BusinessId` (and common filter/sort columns); no N+1; paginate lists with a hard max page size; pass `CancellationToken`; batch sync apply/`SaveChanges`; prefer `ExecuteUpdate`/`ExecuteDelete` only when DeletedItem/sync side-effects are handled. Full rules: [code-quality-performance](../needlebase-architecture/code-quality-performance.md).  
12. **Inspect before coding** — Follow [engineering-review](../needlebase-architecture/engineering-review.md): inspect first, fix root cause (no parallel workarounds), remove obsolete code, verify with evidence.

## Layout

Real folders and target: [solution-layout.md](../needlebase-architecture/solution-layout.md). Api today: `Controllers/` (Auth, Customers, Measurements, Me, Diagnostics), `Http/ApiProblems.cs`, `Middleware/BusinessTenancyMiddleware.cs`, `Program.cs`. Rate limiting: `auth` fixed-window policy (30/min per IP) in `Program.cs`, applied to `AuthController` via `[EnableRateLimiting("auth")]`.

## Auth & roles

- Roles: Owner, Manager, Tailor (per business membership)  
- Policies (in `Infrastructure/DependencyInjection.cs`): `RequireStaff`, `RequireOwner`, `RequireManagerOrOwner`. JWT validation re-checks active membership (`IgnoreQueryFilters` + explicit predicate)  
- Tailor: limit to assigned orders where applicable  

## Sync API (planned S4, contract alignment)

- `POST api/sync/push` — body of queued operations; returns success/fail/conflict per op id  
- Pull — changes since `LastSync` + DeletedItems for business  
- DTOs live in Application contracts; Mobile.SyncService must match  

## Culture & money

Default `en-GH` / GH₵ formatting for MVP; keep consistent with Web/Mobile. Design fields for future currency codes without implementing multi-currency until needed.

## User-facing errors (ProblemDetails)

All errors are **RFC 9457 ProblemDetails** (`application/problem+json`) [code]:
- `AddProblemDetails(...)` adds `traceId`, a default `code` per status (`VALIDATION_FAILED`, `UNAUTHORIZED`, `FORBIDDEN`, `NOT_FOUND`, `RATE_LIMITED`, `SERVER_ERROR`), and generic 5xx copy outside Development.
- `UseExceptionHandler()` (non-Development) + `UseStatusCodePages()` mean empty 401/404/429 bodies also become ProblemDetails. `[ApiController]` model validation returns `ValidationProblem` with `errors`.
- Controllers return `ApiProblems.*` (`NeedleBase.Api/Http/ApiProblems.cs`). **Never** `new { message }`, never bare `NotFound()`.

Rules:
1. `title`/`detail` are end-user English (no claim names, exception text or stack traces). E.g. a missing business claim → 401 `SESSION_INVALID` "Please sign in again".
2. `code` is a stable UPPER_SNAKE extension member; clients switch on it. Auth codes come from `AuthResult.ErrorCode` (`INVALID_CREDENTIALS`, `ACCOUNT_LOCKED`, `REFRESH_INVALID`, …).
3. New error → add a factory in `ApiProblems` + a test asserting `code` (see `AuthEndpointTests.Login_WrongPassword_ReturnsProblemDetailsWithCode`).
4. Use-case services return typed results, never English strings: `OrderResult`/`OrderError` → `ApiProblems.ForOrder` (NotFound/Customer/Measurement → 404; `ORDER_CONFLICT`, `ORDER_LOCKED` → 409; `ORDER_STATUS_INVALID`, `ASSIGNEE_INVALID`, `ORDER_TOTAL_MISMATCH` → 400; other domain validation → 400 `VALIDATION_FAILED`). `CustomerDeleteResult` → 404 or 409 `CUSTOMER_HAS_ORDERS` (pre-check + defensive `DbUpdateException` catch). Copy: `Application/Contracts/Errors/OrderErrorMessages.cs`.
5. **Search** is case-insensitive: `Application/Common/SearchText` (`Lower` for names via `ToLower().Contains`, `Ref` trims `#` and upper-cases for refs). Application can't reference Npgsql `EF.Functions.ILike`, so `lower(...) LIKE` is used on both Postgres and SQLite.
6. Client mapping: [content-and-states.md §5](../needlebase-uiux/content-and-states.md#5-api-errors-to-user-copy).

## Local

```bash
dotnet run --project src/NeedleBase.Api --launch-profile https
```

`docker compose up -d` gives Postgres on localhost:5435. Connection string via user secrets / env var `ConnectionStrings__DefaultConnection`; never commit secrets. Tests: [testing.md](../needlebase-architecture/testing.md).

## Related

- [needlebase-architecture](../needlebase-architecture/SKILL.md)  
- [needlebase-domain](../needlebase-domain/SKILL.md)  
- [needlebase-mobile-sync](../needlebase-mobile-sync/SKILL.md)  
