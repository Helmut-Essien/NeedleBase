---
name: needlebase-api
description: >-
  NeedleBase ASP.NET Core Api + Infrastructure: controllers, JWT/refresh auth
  and policies, fail-closed Business tenancy filters, EF Core PostgreSQL and
  migrations, ProblemDetails errors with stable codes, health/ping, and
  sync push/pull with DeletedItem. Use when editing Api controllers,
  middleware, Program.cs, Infrastructure persistence/identity, EF migrations,
  or API error handling.
---

# NeedleBase API & Infrastructure

Composition: **Api** host + **Infrastructure** implementations of **Application** interfaces. Domain stays persistence-ignorant.

**Built:** auth (register-business, login, refresh, logout), `/api/me`, Customers + Measurements CRUD, Orders CRUD + status patch, health, ping, ProblemDetails. sync push/pull + `DeletedItem` (S4). **Planned:** Payments (S5), Staff/Invitations (S7), Subscription (S8).

## Hard rules

1. **ULID string IDs** (NUlid) — never int/Guid PKs for domain entities.  
2. **BusinessId filter, fail closed** — every tenant entity gets a global filter `x.BusinessId == CurrentBusinessId` set by `BusinessTenancyMiddleware` from the `business_id` claim. **Null business id → no rows.** Login/refresh/membership checks use `.IgnoreQueryFilters()` with explicit `UserId`/`BusinessId` predicates. Every tenant endpoint has a cross-tenant isolation test.  
   [code] Filters are `CurrentBusinessId != null && x.BusinessId == CurrentBusinessId`; `Identity/AuthService.cs` login/refresh membership lookups use `.IgnoreQueryFilters()` scoped by user id. Tests: `TenancyIsolationTests`.  
3. **DeletedItem (planned S4)** — `SaveChanges` will track deletes for mobile pull, with BusinessId. Not in code yet; don't reference it until S4.  
4. **Sync push (S4, built)** — `RequireStaff`; scoped to the token's business by the fail-closed filters; idempotent on client ULIDs (identical replay = applied no-op). No distributed lock (optional; not built).  
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

## Sync API (S4, built)

- **Epoch** (`SyncState` row: Epoch, HighWaterTx; cursor field `e`): a new watermark below HighWaterTx, or below the highest position of a same-epoch cursor, bumps the epoch once (row lock, re-stamps rows/tombstones, HighWaterTx = snapshot xmin). The re-stamp runs in the **background** (one per process, `ApplicationDbContext.StartEpochBump`); meanwhile pull and push answer **503 `SYNC_MAINTENANCE`** + Retry-After 10 s (devices treat it as transient). Tests wait with `ApplicationDbContext.WaitForEpochBumpAsync()`. Mismatched cursor → 400 `SYNC_EPOCH_CHANGED` with extension `reset: SYNC_CURSOR_INVALID` (older apps reset). HighWaterTx is updated in steps of `HighWaterStep` (1000) to avoid a write per pull.
- **Rate limit**: policy `sync`, per user token bucket (burst 300, 10/s), 429 + Retry-After; `UseRateLimiter` runs after auth. Auth policy (login/register/logout) per IP is `RateLimits:AuthPermitPerMinute` (default 30; tests raise it). `refresh-token` has its own per-IP policy `refresh` (`RateLimits:RefreshPermitPerMinute`, default 300) so routine refreshes from a shop behind one IP never use up the login budget.
- **Client IP**: `UseForwardedHeaders` runs first; X-Forwarded-For is trusted only from loopback plus `ForwardedHeaders:KnownProxies` (IPs) / `KnownNetworks` (CIDR). The Web app forwards the browser IP on auth calls (`ForwardClientIpHandler`). Production behind a proxy must set these.
- **Login throttle** (no account lockout): Identity lockout is off and old `LockoutEnd` values are ignored (anyone could lock an owner out). `Infrastructure/Identity/LoginThrottle` counts wrong passwords per email + IP: 5 free, then 30 s doubling to 15 min; forgotten after 1 h; success clears. Throttled → 429 `LOGIN_THROTTLED` + Retry-After. The password is checked before account state, so `ACCOUNT_DEACTIVATED` is only shown to someone who knows the password.
- **Refresh grace**: a rotated token presented again within 30 s, once, while its successor is unused, rotates the successor (plain tokens are not stored, so the client gets a new one); otherwise reuse revokes the family.
- **Sync push is lenient**: SyncService sets `Lenient = true` (`[JsonIgnore]`, never bound from JSON) on every create/update request it hands to the Customer/Measurement/Order services, which then skip the input rules added after data may already be queued on devices (unit price/line-count limits, measurement ranges, phone format, a missing profile without snapshot → line kept without measurements). Domain invariants still apply. REST and the device stay strict. Edits re-check only **changed** measurement values (`AmountRules.EnsureChangedMeasurements`; all of them if the unit changed) and only a **changed** phone, so old over-limit rows keep editable notes.
- **Optimistic concurrency (REST)**: `UpdateCustomerRequest`, `UpdateMeasurementRequest`, `UpdateOrderRequest`, `UpdateOrderStatusRequest` take optional `baseModifiedUtc` (the `dateModifiedUtc` the client loaded). Older → **409 `STALE_UPDATE`** (`Application/Common/StaleUpdate`). Omitted = last write wins (old clients).
- **Phones**: `Domain/Common/PhoneNumbers` — `Normalize` (Ghana → `+233XXXXXXXXX`, `+`/`00` international → `+digits`, anything else kept as typed; never throws) runs in `Customer.Create/Update`; `Check` (strict format) on REST create and changed numbers → 400 `PHONE_INVALID`. `GET api/customers/phone-matches?phone=&excludeId=` lists same-number customers for a non-blocking duplicate warning. Sync replay compares with `PhoneNumbers.Same`.
- **Search** (customers and orders): `Application/Common/CustomerSearchFilter` + `CustomerSearchKeys` (same rule as the device): shadow columns `SearchName` / `PhoneDigits` on Customers, stamped in SaveChanges (migration `CustomerSearchKeysPhone`, PhoneDigits index with `varchar_pattern_ops`). Rows not yet backfilled (null keys) fall back to a contains match. `Infrastructure/Persistence/CustomerKeyBackfillService` (hosted, one-off, idempotent, retries until the migration ran) fills keys and normalises old phones with `ExecuteUpdate` (DateModifiedUtc untouched; the SyncTxId trigger makes devices pull it).
- **Paging**: `PaginatedResponse.Normalize` caps the page so `(page-1)*pageSize` can't overflow (was a 500).

Code: `Api/Controllers/SyncController.cs` → `Application/Sync/SyncService.cs` (reuses Customer/Measurement/Order services, so validation = REST). DTOs: `Application/Contracts/Sync/SyncDtos.cs`.
- Push op: `opId`, `entityType` (customer|measurement|order), `entityId`, `operation` (upsert|delete), `baseModifiedUtc` (null = create). Max 200 ops. Each op runs in its own transaction (`IApplicationDbContext.InTransactionAsync`, inside the Npgsql execution strategy) and commits only when applied, so an order create + status change is atomic. Result per op: `applied` (with server copy) | `conflict` (server copy returned, nothing overwritten): `SYNC_CONFLICT` stale base, `SYNC_DELETED_ELSEWHERE`, `SYNC_PARENT_DELETED` (measurement/order whose customer is gone), `SYNC_DELETE_REFUSED` (customer delete refused because it has orders) | `failed`: rule code, `SYNC_PAYLOAD_INVALID`, or `SYNC_REJECTED` (id taken/unsavable; never reveals another business's ids; foreign update/delete look exactly like a missing id).
- Pull: opaque cursor v2, keyset on `(SyncTxId, Id)`. `SyncTxId` is a shadow bigint set by the Postgres trigger `nb_sync_stamp_tx()` (`pg_current_xact_id()`) on Customers, MeasurementProfiles, Orders and DeletedItems (migration `20261020135519_SyncTxOrder`). Each sync run caps reads at `pg_snapshot_xmin(pg_current_snapshot())`, taken at the start, so a row can never commit behind the cursor (a long-open transaction only delays rows; keep transactions short). Phases customers → measurements → orders(+lines) → deletes. The first pull (no cursor) skips tombstones. Limit default 200, max 500. Bad, old-format, or older-than-retention cursor → 400 `SYNC_CURSOR_INVALID` (the device does a full resync).
- `DeletedItem` tombstones written in `ApplicationDbContext.SaveChanges` for customers, measurements, orders, order lines (including cascades). Re-creating an id removes its tombstone in the same SaveChanges. Tombstones are kept 90 days (`SyncTime.TombstoneRetention`) and purged daily by `Infrastructure/Sync/TombstoneCleanupService`.


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
2. `code` is a stable UPPER_SNAKE extension member; clients switch on it. Auth codes come from `AuthResult.ErrorCode` (`INVALID_CREDENTIALS`, `LOGIN_THROTTLED` (429), `ACCOUNT_DEACTIVATED`, `NO_MEMBERSHIP`, `REFRESH_INVALID`, …). `ACCOUNT_LOCKED` is no longer issued (copy kept for old servers). Other codes added in audit 3: `STALE_UPDATE` (409), `PHONE_INVALID` (400), `SYNC_MAINTENANCE` (503).
3. New error → add a factory in `ApiProblems` + a test asserting `code` (see `AuthEndpointTests.Login_WrongPassword_ReturnsProblemDetailsWithCode`).
4. Use-case services return typed results, never English strings: `OrderResult`/`OrderError` → `ApiProblems.ForOrder` (NotFound/Customer/Measurement → 404; `ORDER_CONFLICT`, `ORDER_LOCKED` → 409; `ORDER_STATUS_INVALID`, `ASSIGNEE_INVALID`, `ORDER_TOTAL_MISMATCH` → 400; other domain validation → 400 `VALIDATION_FAILED`). `CustomerDeleteResult` → 404 or 409 `CUSTOMER_HAS_ORDERS` (pre-check + defensive `DbUpdateException` catch). Copy: `Application/Contracts/Errors/OrderErrorMessages.cs`. Customers/measurements: `RecordResult<T>`/`RecordError` (`Application/Common/RecordResult.cs`, codes + copy in `Contracts/Errors/RecordErrorMessages.cs`) → `ApiProblems.ForRecord`: `CUSTOMER_NOT_FOUND`/`MEASUREMENT_NOT_FOUND` 404, `CUSTOMER_CONFLICT`/`MEASUREMENT_CONFLICT` 409, `STALE_UPDATE` 409, `PHONE_INVALID` 400, other validation 400 `CUSTOMER_SAVE_FAILED`/`MEASUREMENT_SAVE_FAILED` (kept for old clients). `MeasurementService.DeleteAsync` returns `RecordError`. Callers (controllers, `SyncService`, mobile VMs) branch on `.Error`, never on `.Message`; the `(value, message)` deconstruct is for display only. Device local services (`ICustomerLocalService`, `IMeasurementLocalService`) return the same type, plus `STORAGE_FULL` (device-only). Device `IOrderLocalService` returns `RecordResult<OrderDetailDto>` too (`OrderErrorMapping.ToRecord`, order RecordError values carry the `ORDER_*`/`ASSIGNEE_INVALID` codes and `OrderErrorMessages` copy); the server `OrderService` keeps `OrderResult`.
5. **Search**: customer name/phone matching uses the stored keys via `CustomerSearchFilter` (see Search above), the same on Postgres and SQLite. Order refs use `SearchText.Ref` (trims `#`, upper-cases). Don't add new `ToLower().Contains` searches on customers.
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


## Payments (Slice 5)
`GET api/payments`, `GET api/payments/{id}`, `GET|POST api/orders/{orderId}/payments`, `POST api/payments/{id}/void`. Owner/Manager only (403 `PAYMENT_FORBIDDEN`). Codes: `PAYMENT_NOT_FOUND` 404, `PAYMENT_CONFLICT` 409 (same id, different content), `PAYMENT_EXCEEDS_BALANCE` / `PAYMENT_ORDER_LOCKED` 409, `PAYMENT_INVALID` 400. Order DELETE with payments → 409 `ORDER_HAS_PAYMENTS`. Create is idempotent on the client id.

Second audit: `PAYMENT_EXCEEDS_BALANCE` → 409 with the specific detail; `RecordedAtUtc` more than 5 minutes ahead or before 2020 → 400 `PAYMENT_INVALID` (sync push dates it at server time instead); void runs under the payment lock.
