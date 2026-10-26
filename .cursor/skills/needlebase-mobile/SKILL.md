---
name: needlebase-mobile
description: >-
  NeedleBase .NET MAUI Android app: MVVM (CommunityToolkit.Mvvm), AppShell
  routes and nav chrome, MauiProgram DI, auth session/token honesty, local
  SQLite services, MauiIcons.Material icons, and UI performance. Use when
  editing Views, ViewModels, AppShell, MauiProgram, NavigationService,
  AuthService, or mobile screens (Home, Customers, Measurements, Orders,
  Payments, More).
---

# NeedleBase Mobile (MAUI + MVVM)

Primary daily tool for tailors. **Offline-first MVP.** Presentation = MVVM; data/sync = Mobile.Data + Application contracts.

UI screens/tokens: [needlebase-uiux](../needlebase-uiux/SKILL.md) — MAUI patterns: [maui-patterns.md](../needlebase-uiux/maui-patterns.md); copy/formatting/states: [content-and-states.md](../needlebase-uiux/content-and-states.md).  
Sync details: [needlebase-mobile-sync](../needlebase-mobile-sync/SKILL.md).

## Hard rules

1. **Offline session:** Never `ClearTokens()` or force login when refresh fails due to offline, timeout, or non-auth HTTP (5xx/429). Only refresh **401/403** = invalid session. Use `TokenRefreshResult.IsTransientFailure`. Local SQLite must keep working.
2. **Login navigation:** Session expiry uses `ShowLoginAsync()` (hide tabs, pop **every** tab stack to root). Never `GoToAsync("/LoginPage")` alone.
3. **Logout isolation:** Explicit logout clears secure tokens **and** local SQLite for that profile so the next user cannot see prior data. If wipe fails, abort logout. Session expiry must **not** wipe SQLite. Only exception: the server says the account is deactivated or no longer a member (`ACCOUNT_DEACTIVATED`/`NO_MEMBERSHIP` on refresh) → `AccessRevokedService` stops sync, warns with the unsynced count, wipes, shows Login (see needlebase-mobile-sync).
4. **Splash:** If tokens exist and migrations/startup throw, retry — do not treat as logged out.
5. **HTTP 401 retry (planned with `ApiClient`, Slice 3–4):** Buffer body and **clone** `HttpRequestMessage` before resend.
6. **Compiled bindings:** Always `x:DataType` on pages.
7. **Busy guard:** `if (IsBusy) return;` on commands; `try/finally` clears `IsBusy`.
8. **Tab VMs:** Singleton tabs; Transient add/edit forms. Initialize tabs in `OnAppearing`; forms initialize once per instance (do not wipe state on every `OnAppearing`).
9. **Business scope:** All local queries filter `BusinessId` from the signed-in workspace.
10. **UI honesty:** After offline save, show “Saved on this device” — never “synced to cloud”.
11. **Speed:** Compiled bindings (`x:DataType`); virtualize lists (`CollectionView`); keep heavy work off the UI thread; cancel stale loads; prefer in-place collection updates over full rebuilds on hot screens; short-lived DbContext scopes via services. Full rules: [code-quality-performance](../needlebase-architecture/code-quality-performance.md).
12. **Inspect before coding:** Follow [engineering-review](../needlebase-architecture/engineering-review.md): inspect first, fix root cause (no parallel workarounds), remove obsolete code, verify with evidence.
13. **Icons:** UI chrome (Shell tabs, toolbars, list glyphs, empty states) uses **MauiIcons.Material** only — never PNG/SVG MauiImage tab or button icons. See [tab-icons.md](tab-icons.md).
14. **Shell nav bar hidden app-wide:** Set `Shell.NavBarIsVisible="False"` on `AppShell` **and** on every `ContentPage` (plus `NavigationPage.HasNavigationBar="False"`). Titles live **in-page** only — never a Shell title strip. Auth `ShellContent` also keeps `Shell.TabBarIsVisible="False"`. **Pushed** detail/edit pages must provide an in-page **Back** control (`GoBackCommand` → `GoToAsync("..")`) with MauiIcons `ArrowBack` — do not rely on the Shell back button. **Android Back** is handled centrally in `AppShell.OnBackButtonPressed` (page overrides are not reached): it asks the current page's view model first via `IBackAware` (`CanGoBack == false` while saving swallows Back; `TryHandleBack` lets the New order wizard step back), and ignores a second press while a pop is running. New edit/wizard view models with a save must implement `IBackAware`.
15. **No exception text in UI:** local services return friendly copy (`OrderErrorMessages.For(code)`), log technical detail with `Debug.WriteLine`. Never return `ex.Message`.
16. **Local guards mirror the API:** e.g. `ICustomerLocalService.DeleteAsync` returns `CustomerDeleteResult.HasOrders` when orders exist; order totals/refs use the shared `OrderLineFactory` rules.
17. **Lists & local data:** tab lists use the cached-list pattern (`AppearAsync`, `LocalDataVersion`, spinner only when empty); every local write path bumps `LocalDataVersion`; SQLite runs in WAL without `Cache=Shared`; session caches clear in `ClearTokensAsync`. Rules: [lists-and-local-data.md](lists-and-local-data.md).
18. **Typed errors:** local services return `RecordResult<T>`/`RecordError`; VMs branch on `.Error` (code), never on message text. Code list: [needlebase-api](../needlebase-api/SKILL.md) rule 4.
19. **Secondary buttons:** use `Style="{StaticResource SecondaryButton}"` (white, `Gray300` border, Charcoal text); don't hand-colour buttons with `PrimaryLight`.

## MVVM

All VMs inherit `BaseViewModel` (`IsBusy`, `Title`, `CanSubmit`). Patterns, busy guard, validation, lifetimes: [mvvm-patterns.md](mvvm-patterns.md).

## Shell & IA (mobile)

Bottom nav: **Home | Customers | Orders | Payments | More** + FAB **New Order**.  
Role-filter nav (hide Staff/Payments as per domain skill). Built (S7): Payments tab hidden for Tailors (`AppShell.ApplyRoleTabsAsync`, cached role, on every navigation); More → Staff (Owner/Manager, online-only `IStaffApi`), Change password; `AcceptInvitePage` is a root ShellContent like Register. S7b: `ForgotPasswordPage` (root ShellContent; email step → code + new password) linked from Login only when `IAuthService.EmailEnabledAsync()` is true (cached 5 min, hidden when never reached); Staff → member actions include "Move their open orders", also offered right after "Turn off access"; `PasswordRules` delegates to `Application/Common/PasswordPolicy`. Slice 7 pages are code-only (`Views/Ui.cs` builders). Match [needlebase-uiux information-architecture](../needlebase-uiux/information-architecture.md).

### Chrome (all screens)

| Screen type | Shell nav bar | Tab bar | Title / back |
|-------------|---------------|---------|--------------|
| Login / Register / Welcome / other auth | **Hidden** | Hidden | In-page logo + h1 |
| Tab roots (Home, Customers, …) | **Hidden** | Visible | In-page title; top padding clears status bar (~40) |
| Pushed detail / edit forms | **Hidden** | Visible (or hide if form-focused) | In-page title + **Back** button |

**Anti-pattern (learned):** Default Shell nav bar → grey strip repeating page title above designed content. Always verify on device: status bar + page content only (no Shell title bar).

### Icons

MauiIcons.Material only, always sized (tabs `IconSize=22`). Single source: [tab-icons.md](tab-icons.md).

## Where things are

List cache, WAL, warm-up, row speed, NB_PERF, Release AOT: [lists-and-local-data.md](lists-and-local-data.md). Folders, registrations, routes and planned additions: [di-navigation.md](di-navigation.md). BaseViewModel, commands, validation, lifetimes: [mvvm-patterns.md](mvvm-patterns.md). Layout: [solution-layout.md](../needlebase-architecture/solution-layout.md).

ViewModels never touch `LocalDbContext`; they go through `I*LocalService` in Mobile.Data.

Packages: CommunityToolkit.Mvvm, CommunityToolkit.Maui, AathifMahir.Maui.MauiIcons.Material, EF Core Sqlite (Mobile.Data), NUlid. Add heavier UI kits only if they fit the design system.
