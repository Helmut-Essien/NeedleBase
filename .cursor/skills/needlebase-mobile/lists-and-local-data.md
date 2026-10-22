# Lists, local data and device speed (single source)

Learned on a low-end Android phone, 22 Oct 2026. Code: `OrdersViewModel`, `CustomersViewModel`, `Mobile.Data/Data/LocalDataVersion.cs`, `LocalDbConnection`, `LocalSchemaMigrator`, `LocalDatabaseService`, `Helpers/Perf.cs`.

## List cache (tab lists)

- Tab VMs expose `AppearAsync()`; the page's `OnAppearing` calls it (not `LoadCommand`). It keeps the loaded rows when **all** match: business id, `LocalDataVersion.Current`, search, filter, and for date-dependent rows the Accra day (`BusinessTime.Today`); and no error is shown. Otherwise reload **in place** (don't clear first).
- Read `LocalDataVersion.Current` **before** the query and store that value, so a write during the load forces the next reload.
- `ShowBusyIndicator => IsBusy && !IsRefreshing && Items.Count == 0`: spinner only when nothing is on screen; cached rows stay visible during reloads and pull-to-refresh.
- Reset the cache (`ForgetLoaded()`) on error and in `ClearSessionState()` (sign-out/account switch via `ClearAuthSensitiveStateMessage`).
- A filter/search change **supersedes** an in-flight load (bump `_loadGeneration`, clear `IsBusy`, then load), never gets dropped by the `IsBusy` guard (`OrdersViewModel.ReloadForFilterAsync`). Results from a superseded generation or another business id are discarded.
- New list screens copy this pattern; tests: `OrdersListCacheTests`, `CustomersListCacheTests`, `ListCacheRaceTests`.

## Live refresh after a background sync

- `SyncEngine.ApplyPageAsync` calls `LocalDataVersion.NotifyChangedBySync(businessId)` after the commit, **only** when the page changed list rows (tracked Customer/Measurement/Order/OrderLine changes, deletes, prune). Same copy again or an empty page raises nothing.
- Screens use `Helpers/LiveListRefresh`: `Attach()` in `AppearAsync` (captures the UI `SynchronizationContext`), `Detach()` in the page's `OnDisappearing` and in `ClearSessionState`. The event comes from a background thread; the reload is posted to the captured context. Static event, so always detach (no leaks, no reloads for hidden pages).
- Bursts merge: one reload 400 ms after the last event (a large pull = one reload at the end, not one per page).
- `LiveReloadAsync`: skipped while no session or while a search keystroke is pending (that search reads fresh rows); otherwise supersedes any older load (`_loadGeneration`) and runs a **quiet** load (`_silentLoad`: no spinner, empty CTA unchanged, failure keeps rows and shows no error).
- Rows are applied with `Helpers/CollectionSync.Apply` (replace changed positions, add/trim the tail, never `Clear`): no flicker, no scroll jump. Use it for list loads.
- Home uses the same helper for its counts and attention list (fetch everything, then apply; a load generation drops overlapping results).
- Tests: `LiveRefreshTests` (raised only on real changes, burst merge, detach cancels, quiet in-place reload, skipped while typing, error keeps rows), `CollectionSyncTests`.

## LocalDataVersion: every write path must bump it

Process-wide counter. Bumped automatically by: `LocalDbContext.SaveChanges[Async]`; `LocalDataVersionInterceptor` (any EF non-query: `ExecuteUpdate/Delete`, raw SQL); `LocalDataVersionTransactionInterceptor` (again on commit/rollback, since under WAL a read before commit still sees old rows); `LocalSchemaMigrator.MigrateAsync` (raw ADO steps/backfills); `LocalDatabaseService.WipeAsync`. A new write that bypasses EF (raw `SqliteConnection`, file copy/restore) **must** call `LocalDataVersion.Bump()` after it is durable. Spurious bumps only cost one reload; a missed bump shows stale rows.

## In-memory session caches

`AuthService.GetBusinessIdAsync` caches the id in memory (SecureStorage decrypts through the Keystore each read). Cleared only through `ClearTokensAsync` (every sign-out/revoke/refresh-401 path calls it) and replaced in `StoreSessionAsync`; a generation counter stops a slow read re-caching a cleared value. Any new session cache follows the same rule: set on store, clear in `ClearTokensAsync`, never read SecureStorage in loops or per row.

## Background warm-up

Pay EF query compilation off the critical path: `HomeViewModel.WarmOrdersListOnce` runs the Orders list query (`take: 1`) once per process on a background task after Home loads (so after migration and sign-in). Read-only, errors swallowed and retried next Home load. Never warm up writes, never before migration.

## Rows on low-end phones

- Shared, cached `ImageSource`s for icons (`OrderStatusPresentation.RowIconImage`); never one `FontImageSource` per row.
- Precompute display strings in the row model (`OrderListRow`); no `MultiBinding`/converters per row.
- Fixed/simple row layout; `CollectionView` virtualised; avoid nested layouts and per-row triggers.
- Queries run in `Task.Run` (translation is synchronous); fill `Items` once on the UI thread.

## SQLite: WAL rules

- Connection string: `LocalDbConnection.Build` = file + `Default Timeout` (busy timeout). **No `Cache=Shared`** (it serialises readers behind writers).
- `MigrateAsync` sets `PRAGMA journal_mode=WAL` (persistent, outside a transaction, first thing after open), then `PRAGMA wal_checkpoint(TRUNCATE)` so the `-wal` file can't grow across launches. Autocheckpoint stays at SQLite's default.
- Short-lived contexts per operation; one writer (sync semaphore); readers never block on it.
- **Wipe:** `EnsureDeleted` → `SqliteConnection.ClearAllPools()` → delete `-wal`/`-shm`/`-journal` (`LocalDbConnection.SideFiles`) → migrate → bump version. A stale WAL must never replay into a new database.
- Backup/restore/export (none yet): checkpoint `TRUNCATE` first, copy main file only with no writer running, clear pools before replacing, bump the version after.
- Tests: `LocalWalTests`.

## Perf timings (NB_PERF)

`Perf.Log(name, ms)` is `[Conditional("DEBUG")]` → logcat tag `NB_PERF`. Names + milliseconds only: no names, phones, ids or amounts. Measure: `adb logcat -c`, tap the tab with `adb shell input tap X Y`, then `adb logcat -d -s NB_PERF:I`; compare first visit vs repeat (`*.cacheHit`). Report tap→`firstLayoutAfterLoad`.

## Release build

- Android Release: `RunAOTCompilation` + `AndroidEnableProfiledAot` (csproj). Verified on device 22 Oct 2026 (start ~0.9 s to first frame, tabs/detail OK).
- **EF compiled model is deferred until the next EF Core upgrade** (code-gaps G12): `dbcontext optimize` + `UseModel` froze the app at the first migrate on the phone. Re-try only as part of that upgrade, with a drift test and a device start-up test.
- Release needs `-p:NeedleBaseReleaseApiBaseUrl=…` (placeholder `.invalid` otherwise).
