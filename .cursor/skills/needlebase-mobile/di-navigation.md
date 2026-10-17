# DI & Navigation

## MauiProgram registration sketch

```csharp
builder.Services.AddDbContext<LocalDbContext>(...);

builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddSingleton<INavigationService, NavigationService>();
builder.Services.AddSingleton<ISyncService, SyncService>(); // CreateScope per op
builder.Services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
builder.Services.AddSingleton<IOrderLocalService, OrderLocalService>();
builder.Services.AddSingleton<IPaymentLocalService, PaymentLocalService>();

builder.Services.AddTransient<TokenRefreshHandler>();
builder.Services.AddHttpClient("ApiClient", ...)
    .AddHttpMessageHandler<TokenRefreshHandler>();
builder.Services.AddHttpClient("HealthClient", ...); // NO ApiEndpoint rewrite handler

// Tabs — Singleton
builder.Services.AddSingleton<HomeViewModel>();
builder.Services.AddSingleton<CustomersViewModel>();
builder.Services.AddSingleton<OrdersViewModel>();
builder.Services.AddSingleton<PaymentsViewModel>();
builder.Services.AddSingleton<MoreViewModel>();

// Forms — Transient
builder.Services.AddTransient<AddCustomerViewModel>();
builder.Services.AddTransient<CreateOrderViewModel>();
builder.Services.AddTransient<RecordPaymentViewModel>();
```

## Shell navigation

- Tabs in `AppShell.xaml` matching IA: Home, Customers, Orders, Payments, More  
- **Tab icons via MauiIcons.Material** (not PNG): `xmlns:mi` + `xmlns:mci` + `mci:MauiIcon.Value="{mi:Material Icon=…, TargetName='.'}"` on each `<Tab>` — see [tab-icons.md](tab-icons.md). Never reuse `dotnet_bot.png` for all tabs.  
- Prefer:

```xml
<TabBar Route="main">
    <Tab Title="Home" Route="HomePage"
         mci:MauiIcon.Value="{mi:Material Icon=Home, TargetName='.'}">
        <ShellContent ContentTemplate="{DataTemplate views:HomePage}" />
    </Tab>
    <Tab Title="Customers" Route="CustomersPage"
         mci:MauiIcon.Value="{mi:Material Icon=People, TargetName='.'}">
        <ShellContent ContentTemplate="{DataTemplate views:CustomersPage}" />
    </Tab>
    <!-- Orders=ReceiptLong, Payments=Payments, More=Menu — same pattern -->
</TabBar>
```

- Register routes for non-tab pages: CreateOrder, CustomerDetail, OrderDetail, SyncDetails, Settings, MeasurementEntry  
- FAB → `GoToAsync` CreateOrder route  

### Auth / pre-shell pages (nav chrome)

Login, Register, and other pre-shell routes must not show Shell’s top title bar (duplicate “Sign in” / “Register” strip).

```xml
<!-- On the ContentPage -->
Shell.NavBarIsVisible="False"
NavigationPage.HasNavigationBar="False"

<!-- On AppShell ShellContent for that route -->
Shell.TabBarIsVisible="False"
Shell.NavBarIsVisible="False"
FlyoutItemIsVisible="False"
```

Verify on device: auth screens = status bar + designed content only (logo, in-page title, form). No grey/dark Shell title bar.

### Session expiry

`ShowLoginAsync()` must:

1. Dismiss modals  
2. Pop **each tab** navigation stack to root  
3. Hide tab bar / show login host  
4. Land on Login with nav bar still hidden (auth `ShellContent` / page flags above)  

### Query properties

```csharp
[QueryProperty(nameof(OrderId), "OrderId")]
public partial class OrderDetailViewModel : BaseViewModel { ... }
```

## Auth clients

- **AuthClient** — login/refresh (no bearer retry loop issues)  
- **ApiClient** — authorized calls + TokenRefreshHandler  
- **HealthClient** — connectivity ping without endpoint rewrite handlers  

Primary/fallback API URLs via configuration; never hardcode production hosts.
