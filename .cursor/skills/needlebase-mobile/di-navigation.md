# DI & navigation (current code + planned)

## Current registrations (`src/NeedleBase.Mobile/MauiProgram.cs`)

```csharp
builder.Services.Configure<ApiSettings>(options => { options.BaseUrl = /* #if DEBUG */ "http://localhost:5036/" /* #else */ ApiEndpoint.ReleaseBaseUrl(); });
builder.Services.AddSingleton(new LocalDbOptions { DbPath = Path.Combine(FileSystem.AppDataDirectory, "needlebase.db3") });
builder.Services.AddDbContext<LocalDbContext>(...UseSqlite($"Data Source={path};Cache=Shared"));
builder.Services.AddHttpClient("AuthClient", c => c.Timeout = TimeSpan.FromSeconds(30));

// Singletons: app-lifetime services + tab VMs/pages
ISecureStorageService→MauiSecureStorageService, IAuthService→AuthService, INavigationService→NavigationService,
ILocalDatabaseService→LocalDatabaseService, ICustomerLocalService→CustomerLocalService,
IMeasurementLocalService→MeasurementLocalService, AppStartupService,
HomeViewModel, CustomersViewModel, HomePage, CustomersPage

// Transients: auth + forms + pushed pages
LoginViewModel, RegisterViewModel, CustomerEditViewModel, CustomerDetailViewModel, MeasurementEditViewModel,
LoginPage, RegisterPage, TermsPage, PrivacyPage, CustomerEditPage, CustomerDetailPage, MeasurementEditPage,
OrdersPlaceholderPage, MorePlaceholderPage
```

Debug Android uses `adb reverse tcp:5036 tcp:5036`. Release reads the MSBuild property `NeedleBaseReleaseApiBaseUrl` (https only; stamped as assembly metadata, read by `Configuration/ApiEndpoint.cs`). Default is the placeholder `https://api.needlebase-placeholder.invalid/`; pass the real host: `dotnet publish -f net9.0-android -c Release -p:NeedleBaseReleaseApiBaseUrl=https://<api-host>/`.

Local SQLite schema: `ILocalDatabaseService.MigrateAsync()` at startup runs `LocalSchemaMigrator` (forward-only steps, version in `PRAGMA user_version`; pre-versioning `EnsureCreated` installs are baselined as v1). **Every `LocalDbContext` model change appends a new step**; never edit a shipped step. `LocalSchemaMigratorTests` fails on drift.


Pages resolve their VM with `ServiceHelper.GetRequiredService<TViewModel>()` in the constructor (e.g. `HomePage`). Local services are Singletons that create a short `LocalDbContext` scope per operation.

## Shell & routes (`AppShell.xaml` / `.xaml.cs`)

- Auth `ShellContent`s: `LoginPage`, `RegisterPage` (tab bar hidden).
- `<TabBar Route="main">` tabs: `HomePage`, `CustomersPage`, `OrdersPage` (→ `OrdersPlaceholderPage`), `PaymentsPage` (`PaymentsViewModel` singleton, cached + LiveListRefresh; routes `RecordPaymentPage`, `PaymentReceiptPage`), `MorePage` (→ `MorePlaceholderPage`, with About → Terms/Privacy). Icons: [tab-icons.md](tab-icons.md).
- Registered push routes: `CustomerEditPage`, `CustomerDetailPage`, `MeasurementEditPage`, `TermsPage`, `PrivacyPage`.
- Parameters: query string + `IQueryAttributable` on the VM, e.g. `GoToAsync($"CustomerDetailPage?customerId={Uri.EscapeDataString(id)}")`. Always escape ids.
- Back: in-page Back → `Shell.Current.GoToAsync("..")`. Android hardware Back + double-back-to-exit is central in `AppShell.OnBackButtonPressed` ([maui-patterns.md](../needlebase-uiux/maui-patterns.md#android-back-double-press-to-exit)).
- Nav chrome rules: [SKILL.md hard rule 14](SKILL.md) (single source).
- Startup: `AppShell.Loaded` → `AppStartupService.RunOnceAsync` (DB init, session gate → `ShowMainAsync`/`ShowLoginAsync`).

## Session navigation (`NavigationService`)

`ShowLoginAsync()`: dismiss overlays, pop every tab stack to root, send `ClearAuthSensitiveStateMessage` (WeakReferenceMessenger), land on Login. `ShowMainAsync()`: `GoToAsync("//main/HomePage")`. Never `GoToAsync("/LoginPage")` alone.

## Planned (not in code; add with its slice)

| Item | Slice |
|------|-------|
| `"ApiClient"` HttpClient + mobile `TokenRefreshHandler` (buffer + clone body on 401 retry) for authorised calls; `"HealthClient"` for `/api/test/ping` without refresh handler | 3–4 |
| `ISyncService`/`SyncService` Singleton orchestrator | 4 |
| `IOrderLocalService`, `OrdersViewModel` (Singleton tab), `CreateOrderViewModel`/`OrderDetailViewModel` (Transient), routes `CreateOrderPage`, `OrderDetailPage`; FAB → `CreateOrderPage` | 3 |
| `IPaymentLocalService`, `PaymentsViewModel`, `RecordPaymentViewModel` | 5 |
| `MoreViewModel` + `SyncDetailsPage`, `SettingsPage` | 4/8 |

Primary/fallback Api URLs come from configuration; never hard-code production hosts.
