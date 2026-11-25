# DI and navigation (current code)

How the MAUI app registers services and navigates. Registrations live in `src/NeedleBase.Mobile/MauiProgram.cs`.

## Current registrations

- **Config and storage:** `ApiSettings.BaseUrl` is `http://localhost:5036/` in Debug, or `ApiEndpoint.ReleaseBaseUrl()` in Release. `LocalDbOptions` + `LocalDbContext` (SQLite, `Cache=Shared`) at `FileSystem.AppDataDirectory/needlebase.db3`.
- **HTTP:** `AuthClient` (30 s, sign-in and token refresh). `ApiClient` (`HttpSyncApi.ClientName`, 60 s) with `TokenRefreshHandler` for authorised calls (buffers the body and retries once on 401). A separate client for app updates. There is no `HealthClient`.
- **Singletons (app lifetime and tab VMs/pages):** secure storage, `IAuthService`, `INavigationService`, `ILocalDatabaseService`, customer/measurement/order/payment local services, `IFreeModeLocalService`/`IFreeCopyService`/`IFreeUpgradeLocalService`, `SubscriptionStatusStore`, `ISyncApi`/`SyncEngine`/`SyncService`, WhatsApp consent sync, `SyncStatusViewModel`, `AppStartupService`, `AccessRevokedService`, `StaffApi` (also exposed as staff, WhatsApp opened/admin and consent APIs), app-update service, `HomeViewModel`/`HomePage`, `CustomersViewModel`/`CustomersPage`, `OrdersViewModel`/`OrdersPage`, `PaymentsViewModel`/`PaymentsPage`, `MorePage`.
- **Transients (auth, forms and pushed pages):** login, register, free setup, free upgrade, forgot password, accept invite, customer and measurement edit/detail, create/edit/detail order, record payment and receipt, staff, WhatsApp settings, change password, delete account, terms, privacy, and `SyncDetailsPage`.

Debug Android uses `adb reverse tcp:5036 tcp:5036`. Release reads the MSBuild property `NeedleBaseReleaseApiBaseUrl` (https only; stamped as assembly metadata, read by `Configuration/ApiEndpoint.cs`). Default is the placeholder `https://api.needlebase-placeholder.invalid/`; pass the real host: `dotnet publish -f net9.0-android -c Release -p:NeedleBaseReleaseApiBaseUrl=https://<api-host>/`.

Local SQLite schema: `ILocalDatabaseService.MigrateAsync()` at startup runs `LocalSchemaMigrator` (forward-only steps, version in `PRAGMA user_version`; pre-versioning `EnsureCreated` installs are baselined as v1). **Every `LocalDbContext` model change appends a new step**; never edit a shipped step. `LocalSchemaMigratorTests` fails on drift.

Pages resolve their VM with `ServiceHelper.GetRequiredService<TViewModel>()` in the constructor (e.g. `HomePage`). Local services are Singletons that create a short `LocalDbContext` scope per operation.

## Shell & routes (`AppShell.xaml` / `.xaml.cs`)

- Auth `ShellContent`s (tab bar hidden): `LoginPage`, `RegisterPage`, `ForgotPasswordPage`, `AcceptInvitePage`.
- `<TabBar Route="main">` tabs: `HomePage`, `CustomersPage`, `OrdersPage`, `PaymentsPage`, `MorePage`. Icons: [tab-icons.md](tab-icons.md). New order is a page action on Home, Orders and a customer page, not a FAB.
- Registered push routes: `CustomerEditPage`, `CustomerDetailPage`, `MeasurementEditPage`, `CreateOrderPage`, `OrderDetailPage`, `EditOrderPage`, `RecordPaymentPage`, `PaymentReceiptPage`, `SyncDetailsPage`, `WhatsAppSettingsPage`, `StaffPage`, `ChangePasswordPage`, `DeleteAccountPage`, `TermsPage`, `PrivacyPage`, `FreeSetupPage`, `FreeUpgradePage`.
- Parameters: query string + `IQueryAttributable` on the VM, e.g. `GoToAsync($"CustomerDetailPage?customerId={Uri.EscapeDataString(id)}")`. Always escape ids.
- Back: in-page Back → `Shell.Current.GoToAsync("..")`. Android hardware Back + double-back-to-exit is central in `AppShell.OnBackButtonPressed` ([maui-patterns.md](../needlebase-uiux/maui-patterns.md#android-back-double-press-to-exit)).
- Nav chrome rules: [SKILL.md hard rule 14](SKILL.md) (single source).
- Startup: `AppShell.Loaded` → `AppStartupService.RunOnceAsync` (DB init, session gate → `ShowMainAsync`/`ShowLoginAsync`).

## Session navigation (`NavigationService`)

`ShowLoginAsync()`: dismiss overlays, pop every tab stack to root, send `ClearAuthSensitiveStateMessage` (WeakReferenceMessenger), land on Login. `ShowMainAsync()`: `GoToAsync("//main/HomePage")`. Never `GoToAsync("/LoginPage")` alone.

## Free phone-only and upgrade (built)

- **Free:** Login → Use free on this phone → `FreeSetupPage`; `AuthService.StartFreeModeAsync` seeds a local ULID BusinessId + `LocalFreeModeGate` (sync skipped). More → Save a copy.
- **Upgrade:** More → Go online with a licence key → `FreeUpgradePage` → `UpgradeFromFreeAsync` (register-business, remap BusinessId, clear Free without wipe). `free_upgrade_pending` + `TryResumeFreeUpgradeAsync` on startup if killed mid-way. `ShowMainAsync` starts sync so `ServerKnown=false` rows upload.

Owner More shows plan/staff usage from the last sync and **Export shop data** (`GET /api/export`). Free Home/More show compact meters (`7/10 customers · 3/20 orders`). See [needlebase-plans](../needlebase-plans/SKILL.md).

Primary/fallback Api URLs come from configuration; never hard-code production hosts.
