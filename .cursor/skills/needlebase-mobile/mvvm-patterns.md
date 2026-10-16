# MVVM Patterns

## BaseViewModel

Provide at least:

- `IsBusy` / `IsNotBusy` / `Title`
- `ValidateTokenAsync` — for operations that **require** online API
- `TryValidateTokenSilentAsync` — offline-tolerant; returns true if local session should continue
- `NavigateToLoginAsync` → `INavigationService.ShowLoginAsync()`

Local CRUD should **not** block on online token validation; only sync/push and explicit online calls should.

## Commands

- `[RelayCommand]` private async Task methods  
- Disable when `IsBusy` or validation fails (`CanExecute` where useful)  
- Catch exceptions → user-visible alert/snackbar with plain language  

## Collections

- `ObservableCollection<T>` for lists  
- Prefer list item DTOs/models shaped for UI (name, phone, status chip, outstanding)  
- Pull-to-refresh: separate `IsRefreshing` from first-load `IsBusy` (skeletons)

## Page code-behind

Keep thin:

```csharp
public OrdersPage(OrdersViewModel vm)
{
    InitializeComponent();
    BindingContext = vm;
}

protected override async void OnAppearing()
{
    base.OnAppearing();
    await ((OrdersViewModel)BindingContext).LoadAsync();
}
```

Add/Edit: initialize once; on later appearances only refresh lookups.

## Messaging

Prefer:

- Sync progress events on `ISyncService` (AssetTag style), or  
- `WeakReferenceMessenger` if introduced intentionally  

Do not leave an empty Messages folder unused — choose one approach.

## Measurement entry

- Numeric `Keyboard` / `InputScope`  
- Tabular-friendly fields  
- Save goes to local service → queue sync → “Saved on this device” if offline  

## Testing ViewModels

xUnit + substitutes for `IOrderLocalService`, `IAuthService`, `INavigationService`. Assert commands update collections and busy flags; assert offline save does not call navigation to login on transient refresh failure.
