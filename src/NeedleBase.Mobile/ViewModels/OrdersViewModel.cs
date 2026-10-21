using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Helpers;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

public sealed class StatusFilterOption
{
    public StatusFilterOption(string? status, string display)
    {
        Status = status;
        Display = display;
    }

    public string? Status { get; }
    public string Display { get; }
}

public partial class OrdersViewModel : BaseViewModel
{
    private readonly IOrderLocalService _orders;
    private readonly IAuthService _auth;
    private int _loadGeneration;
    private readonly SearchDebouncer _searchDebounce = new();
    private bool _suppressFilterReload = true;

    public OrdersViewModel(IOrderLocalService orders, IAuthService auth, SyncService? sync = null)
    {
        _sync = sync;
        _orders = orders;
        _auth = auth;
        Title = "Orders";
        StatusFilters.Add(new StatusFilterOption(null, "All"));
        foreach (var status in OrderStatus.All)
        {
            StatusFilters.Add(new StatusFilterOption(status, OrderStatus.DisplayName(status)));
        }

        SelectedStatusFilter = StatusFilters[0];
        _suppressFilterReload = false;
    }

    public ObservableCollection<OrderListRow> Items { get; } = new();
    public ObservableCollection<StatusFilterOption> StatusFilters { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyAdd))]
    private string searchText = string.Empty;

    [ObservableProperty]
    private StatusFilterOption? selectedStatusFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBusyIndicator))]
    private bool isRefreshing;

    public bool ShowBusyIndicator => IsBusy && !IsRefreshing;

    [ObservableProperty]
    private string emptyMessage = "Create your first order to track sewing work.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyAdd))]
    private string? errorMessage;

    [ObservableProperty]
    private OrderListRow? selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyAdd))]
    private bool hasSession;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool ShowEmptyAdd =>
        !IsBusy && HasSession && !HasError && string.IsNullOrWhiteSpace(SearchText) &&
        SelectedStatusFilter?.Status is null;

    protected override void OnBusyChanged()
    {
        OnPropertyChanged(nameof(ShowEmptyAdd));
        OnPropertyChanged(nameof(ShowBusyIndicator));
    }

    partial void OnSelectedItemChanged(OrderListRow? value)
    {
        if (value is null)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await OpenAsync(value);
            SelectedItem = null;
        });
    }

    partial void OnSelectedStatusFilterChanged(StatusFilterOption? value)
    {
        if (_suppressFilterReload)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () => await LoadAsync());
    }

    public void ClearSessionState()
    {
        Interlocked.Increment(ref _loadGeneration);
        Items.Clear();
        SearchText = string.Empty;
        _searchDebounce.Cancel();
        SelectedItem = null;
        ErrorMessage = null;
        HasSession = false;
        IsBusy = false;
        IsRefreshing = false;
        _suppressFilterReload = true;
        SelectedStatusFilter = StatusFilters[0];
        _suppressFilterReload = false;
        EmptyMessage = "Create your first order to track sewing work.";
        OnPropertyChanged(nameof(ShowEmptyAdd));
    }

    /// <summary>Search-as-you-type: debounced, stale queries cancelled (see SearchDebouncer).</summary>
    partial void OnSearchTextChanged(string value)
    {
        if (!HasSession)
        {
            return;
        }

        _ = _searchDebounce.Run(async cancellationToken =>
        {
            // Supersede any in-flight load so its older results can't overwrite these.
            Interlocked.Increment(ref _loadGeneration);
            IsBusy = false;
            await LoadCoreAsync(cancellationToken);
        });
    }

    [RelayCommand]
    private Task LoadAsync() => LoadCoreAsync(CancellationToken.None);

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        var generation = _loadGeneration;
        IsBusy = true;
        OnPropertyChanged(nameof(ShowEmptyAdd));
        ErrorMessage = null;
        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            if (generation != _loadGeneration)
            {
                return;
            }

            HasSession = !string.IsNullOrWhiteSpace(businessId);
            if (!HasSession)
            {
                ClearSessionState();
                EmptyMessage = "Sign in to see orders on this device.";
                ErrorMessage = "Missing business session.";
                return;
            }

            var search = SearchText;
            var status = SelectedStatusFilter?.Status;
            var list = await Task.Run(
                () => _orders.SearchAsync(businessId!, search, status, cancellationToken: cancellationToken),
                cancellationToken);
            if (generation != _loadGeneration)
            {
                return;
            }

            var currentBusinessId = await _auth.GetBusinessIdAsync();
            if (generation != _loadGeneration ||
                !string.Equals(currentBusinessId, businessId, StringComparison.Ordinal))
            {
                return;
            }

            Items.Clear();
            foreach (var item in list)
            {
                Items.Add(new OrderListRow(item));
            }

            EmptyMessage = string.IsNullOrWhiteSpace(search) && status is null
                ? "Create your first order to track sewing work."
                : "No orders match that filter.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer search replaced this one.
        }
        catch (Exception)
        {
            if (generation != _loadGeneration)
            {
                return;
            }

            // Keep cached rows if any; show inline error with pull-to-retry.
            ErrorMessage = "Couldn't load orders. Pull to try again.";
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                IsBusy = false;
                IsRefreshing = false;
                OnPropertyChanged(nameof(ShowEmptyAdd));
            }
        }
    }

    private readonly SyncService? _sync;

    /// <summary>Pull-to-refresh syncs first (bounded wait), then reloads from the device.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            IsRefreshing = false;
            return;
        }

        IsRefreshing = true;
        if (_sync is not null)
        {
            await _sync.SyncForRefreshAsync();
        }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task SearchAsync() => await LoadAsync();

    [RelayCommand]
    private async Task NewOrderAsync() =>
        await Shell.Current.GoToAsync(nameof(Views.CreateOrderPage));

    [RelayCommand]
    private async Task OpenAsync(OrderListRow? item)
    {
        if (item is null)
        {
            return;
        }

        await Shell.Current.GoToAsync(
            $"{nameof(Views.OrderDetailPage)}?orderId={Uri.EscapeDataString(item.Id)}");
    }
}
