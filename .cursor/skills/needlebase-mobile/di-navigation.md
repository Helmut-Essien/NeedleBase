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
- Register routes for non-tab pages: CreateOrder, CustomerDetail, OrderDetail, SyncDetails, Settings, MeasurementEntry  
- FAB → `GoToAsync` CreateOrder route  

### Session expiry

`ShowLoginAsync()` must:

1. Dismiss modals  
2. Pop **each tab** navigation stack to root  
3. Hide tab bar / show login host  

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
