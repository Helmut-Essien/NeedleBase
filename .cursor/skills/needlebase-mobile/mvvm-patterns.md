# MVVM patterns (current code)

## BaseViewModel (`ViewModels/BaseViewModel.cs`)

```csharp
public partial class BaseViewModel : ObservableObject
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit))] private bool isBusy;
    [ObservableProperty] private string title = string.Empty;
    public bool CanSubmit => !IsBusy;
    partial void OnIsBusyChanged(bool value) => OnBusyChanged();
    protected virtual void OnBusyChanged() { }   // derived VMs recompute e.g. CanSave
}
```

That's all it has. Don't call `ValidateTokenAsync`/`NavigateToLoginAsync` on it; they don't exist. Session handling lives in `IAuthService` (`RefreshAsync` → `TokenRefreshResult.IsTransientFailure`) and `INavigationService.ShowLoginAsync()`. Local CRUD never blocks on token validation.

## Commands

- `[RelayCommand] private async Task XAsync()`, guarded with `if (IsBusy) return; IsBusy = true; try { … } finally { IsBusy = false; }`.
- Bind `IsEnabled="{Binding CanSubmit}"` (or a derived `CanSave`).
- Catch expected failures → plain-language error property / `DisplayAlert`. Copy rules: [content-and-states](../needlebase-uiux/content-and-states.md).

## Field validation

`XError` + `HasXError` + `partial void OnXChanged(...) => XError = null;` (see `CustomerEditViewModel`). Full pattern: [validation.md](../needlebase-uiux/validation.md).

## Navigation parameters

Implement `IQueryAttributable.ApplyQueryAttributes` (as `CustomerDetailViewModel`, `CustomerEditViewModel`, `MeasurementEditViewModel` do). Escape ids in query strings.

## Lifetimes & loading

- Tab VMs (`HomeViewModel`, `CustomersViewModel`) are Singletons: load in the page's `OnAppearing` via `LoadCommand`; cancel or ignore stale loads.
- Form VMs are Transient: initialise once per instance from query attributes; don't wipe user input on every `OnAppearing`.
- Logout clears singleton state via `ClearAuthSensitiveStateMessage` (WeakReferenceMessenger, `Messaging/`). That's the chosen messaging approach; don't add a second one.

## Collections

`ObservableCollection<T>` of UI-shaped items; `CollectionView` (virtualised) with `EmptyView`. Separate `IsRefreshing` (pull-to-refresh) from first-load `IsBusy` when pull-to-refresh is added.

## Testing

`tests/NeedleBase.Mobile.Tests`: construct VMs with real local services over EF Sqlite or hand fakes (`HomeViewModelTests`, `PasswordToggleViewModelTests`). Assert busy flags, errors, and that transient refresh failure never forces login. See [testing.md](../needlebase-architecture/testing.md).

## Planned

Sync progress → `ISyncService.SyncProgressChanged` event consumed by Home SyncChip (Slice 4).
