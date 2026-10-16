---
name: needlebase-mobile
description: >-
  Implements NeedleBase .NET MAUI mobile app with Clean Architecture boundaries,
  MVVM (CommunityToolkit.Mvvm), and UI performance practices. Use when working
  on Views, ViewModels, AppShell, MauiProgram DI, navigation, auth session, or
  mobile UI wiring for Home, Customers, Orders, Payments, More.
---

# NeedleBase Mobile (MAUI + MVVM)

Primary daily tool for tailors. **Offline-first MVP.** Presentation = MVVM; data/sync = Mobile.Data + Application contracts.

UI screens/tokens: [needlebase-uiux](../needlebase-uiux/SKILL.md).  
Sync details: [needlebase-mobile-sync](../needlebase-mobile-sync/SKILL.md).

## Hard rules (from AssetTag lessons)

1. **Offline session:** Never `ClearTokens()` or force login when refresh fails due to offline, timeout, or non-auth HTTP (5xx/429). Only refresh **401/403** = invalid session. Use `TokenRefreshResult.IsTransientFailure`. Local SQLite must keep working.
2. **Login navigation:** Session expiry uses `ShowLoginAsync()` (hide tabs, pop **every** tab stack to root). Never `GoToAsync("/LoginPage")` alone.
3. **Logout isolation:** Explicit logout clears secure tokens **and** local SQLite for that profile so the next user cannot see prior data. If wipe fails, abort logout. Session expiry must **not** wipe SQLite.
4. **Splash:** If tokens exist and migrations/startup throw, retry — do not treat as logged out.
5. **HTTP 401 retry:** Buffer body and **clone** `HttpRequestMessage` before resend.
6. **Compiled bindings:** Always `x:DataType` on pages.
7. **Busy guard:** `if (IsBusy) return;` on commands; `try/finally` clears `IsBusy`.
8. **Tab VMs:** Singleton tabs; Transient add/edit forms. Initialize tabs in `OnAppearing`; forms initialize once per instance (do not wipe state on every `OnAppearing`).
9. **Business scope:** All local queries filter `BusinessId` from the signed-in workspace.
10. **UI honesty:** After offline save, show “Saved on this device” — never “synced to cloud”.
11. **Speed:** Compiled bindings (`x:DataType`); virtualize lists (`CollectionView`); keep heavy work off the UI thread; cancel stale loads; prefer in-place collection updates over full rebuilds on hot screens; short-lived DbContext scopes via services. Full rules: [code-quality-performance](../needlebase-architecture/code-quality-performance.md).
12. **Inspect before coding:** Follow [engineering-review](../needlebase-architecture/engineering-review.md): inspect first, fix root cause (no parallel workarounds), remove obsolete code, verify with evidence.

## MVVM (CommunityToolkit.Mvvm)

All ViewModels inherit `BaseViewModel` : `ObservableObject`.

```csharp
public partial class OrdersViewModel : BaseViewModel
{
    private readonly IOrderLocalService _orders; // Application-facing mobile service
    private readonly IAuthService _auth;

    [ObservableProperty]
    private ObservableCollection<OrderListItem> orders = new();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            // Local-first: read SQLite even if token refresh is transiently failing
            var items = await _orders.GetOrdersAsync();
            Orders = new ObservableCollection<OrderListItem>(items);
        }
        finally { IsBusy = false; }
    }
}
```

Patterns: [mvvm-patterns.md](mvvm-patterns.md), [di-navigation.md](di-navigation.md).

## Shell & IA (mobile)

Bottom nav: **Home | Customers | Orders | Payments | More** + FAB **New Order**.  
Role-filter nav (hide Staff/Payments as per domain skill). Match [needlebase-uiux information-architecture](../needlebase-uiux/information-architecture.md).

## Project folders

```
NeedleBase.Mobile/
  Views/ ViewModels/ Services/ Controls/ Converters/ Configuration/
  MauiProgram.cs  AppShell.xaml
NeedleBase.Mobile.Data/
  Data/LocalDbContext.cs  Migrations/
```

ViewModels **do not** reference `LocalDbContext` directly — go through services/use-case facades (Clean Architecture).

## DI lifetimes (document reality)

AssetTag code uses **Singleton** sync/data services that `CreateScope()` per operation so one queue/orchestrator lives for the app lifetime. Prefer that pattern **or** true Scoped services — pick one and keep skills/code aligned.

| Registration | Examples |
|--------------|----------|
| Singleton | Auth, Navigation, Sync orchestrator, Network, tab ViewModels/Pages |
| Transient | Add/Edit ViewModels/Pages, TokenRefreshHandler |
| Scoped | `LocalDbContext` (resolve via scope inside singleton services) |

## Packages

CommunityToolkit.Mvvm, CommunityToolkit.Maui, EF Core SQLite (Mobile.Data), NUlid, JWT handling. Add UI kits only if they fit NeedleBase design system (prefer simple MAUI + styles from uiux skill over copying AssetTag Uranium/Syncfusion unless needed).

## Important paths (once scaffolded)

- `src/NeedleBase.Mobile/MauiProgram.cs`
- `src/NeedleBase.Mobile/AppShell.xaml(.cs)`
- `src/NeedleBase.Mobile/ViewModels/BaseViewModel.cs`
- `src/NeedleBase.Mobile/Services/`
- `src/NeedleBase.Mobile.Data/Data/LocalDbContext.cs`
