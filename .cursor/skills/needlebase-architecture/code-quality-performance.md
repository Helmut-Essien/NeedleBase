# Code quality & performance (NeedleBase)

All generated NeedleBase code must follow these standards. Prefer measurable speed (fewer allocations, fewer round-trips, less UI jank) over premature micro-optimizations that hurt clarity.

**Before any change:** follow [engineering-review.md](engineering-review.md) — inspect first, fix root cause, remove obsolete code, verify with evidence.

## Universal rules

1. **Correct first, then hot-path speed** — Fix correctness and tenancy; then remove N+1 queries, sync, and UI bind waste on real paths.
2. **Async all the way** — `async`/`await` for I/O; never `.Result` / `.Wait()` on UI or request threads. Prefer `ConfigureAwait(false)` in Application/Infrastructure libraries; omit on MAUI UI code.
3. **No sync-over-async** — Especially in constructors, DI factories, and MAUI lifecycle.
4. **Allocate deliberately** — Prefer `IAsyncEnumerable` / streaming for large sets; reuse buffers; avoid LINQ materialization in tight loops when a simple `for`/`foreach` is clearer and faster.
5. **Cancellation** — Propagate `CancellationToken` on API, sync, HTTP, and long EF queries.
6. **Fail fast, log once** — Structured exceptions; do not swallow; do not log-and-rethrow the same message at every layer. Use `ILogger<T>` message templates; never log passwords, tokens, auth headers or customer PII (log ULIDs instead).
7. **Immutability at boundaries** — DTOs as `record`; no mutable shared state across requests without locks/semaphores.
8. **DI honesty** — Document real lifetimes; never capture Scoped services in Singletons without `IServiceScopeFactory`.
9. **Tests for hot paths** — Auth, BusinessId filtering, sync idempotency, payment conflict. Required list: [testing.md](testing.md).

## EF Core / SQL (Api + Mobile.Data)

| Do | Don't |
|----|--------|
| `AsNoTracking()` for read-only queries | Track entities you never update |
| Project to DTOs (`Select`) early | Load full graphs then map |
| Indexed `BusinessId` (+ FK/date as needed) | Unfiltered table scans |
| Explicit `Include` / split queries when needed | Accidental N+1 via lazy load |
| Batch sync apply (e.g. 200) + single `SaveChanges` where safe | One `SaveChanges` per row in loops |
| Compiled queries for hottest repeated reads | Rebuilding identical expression trees every call |
| Parameterized queries only | String-concat SQL |

- Prefer `ExecuteUpdate`/`ExecuteDelete` for bulk server-side mutations when entity events/sync queue are not required.
- Mobile: keep `LocalDbContext` scopes short; dispose promptly after each sync/op scope.

## ASP.NET Core API

- Thin controllers → Application use cases; no business logic in endpoints.
- Response compression / output caching only where data is safe (never cross-tenant cache without keying on `BusinessId`).
- Pagination on list endpoints (default page size; hard max).
- Avoid over-fetching: separate list DTOs vs detail DTOs.
- Correct status codes; errors via `ApiProblems.*` ProblemDetails with `code`.
- Sync endpoints: idempotent by ULID; distributed lock; stream or chunk large payloads.

## MAUI / MVVM

- Always `x:DataType` compiled bindings; avoid reflection bindings.
- Update `ObservableCollection` in place when possible; avoid wholesale replace on every poll if diffing is cheap.
- Virtualize long lists (`CollectionView`); never bind huge graphs to one page.
- Offload heavy work off UI thread; marshal property updates to main thread once.
- Images: cache, downsample; do not decode full-res for thumbnails.
- Tab VMs: load incrementally / visible-first; cancel prior loads on re-appear when stale.
- Mobile list cache, local change counter, WAL, row rules, warm-up and NB_PERF measuring: [lists-and-local-data.md](../needlebase-mobile/lists-and-local-data.md). Measure on the phone before and after a speed change.

## HTTP clients

- Named clients with timeouts, Polly-style retry only for idempotent/transient failures.
- Buffer/clone request bodies before 401 retry (mobile hard rule).
- Prefer JSON source generators (`JsonSerializerContext`) for hot DTOs when practical.
- Compress sync payloads when large; avoid chatty per-entity HTTP when batch APIs exist.

## Sync path (critical)

- Single orchestrator; semaphore so push/pull do not thrash SQLite.
- Batch push ops; batch pull apply; one transaction per batch when safe.
- `SuppressSyncQueue` in `finally` always.
- Skip no-op patches; do not enqueue unchanged entities.
- Index queue by `BusinessId`, `CreatedAt`, status for fast pending counts.

## Web

- Server-side pagination/filter for tables; no download-all-then-filter in browser for MVP-scale growth.
- Defer non-critical dashboard widgets; parallelize independent HTTP calls with `Task.WhenAll`.
- Cache static assets; avoid large ViewState/session blobs.
- Keep HttpClient handlers light and non-blocking.

## What “highly optimised” does NOT mean

- Do not add caching layers, message buses, or CQRS/Mediator solely for “speed” without a measured need.
- Do not micro-optimize Domain invariants or one-off admin paths.
- Do not sacrifice BusinessId isolation, payment honesty, or offline correctness for throughput.
