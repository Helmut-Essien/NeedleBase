using NeedleBase.Mobile.Data.Data;
using NeedleBase.Application.Common;
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

public partial class OrdersViewModel : BaseViewModel, IQueryAttributable
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
        StatusFilters.Add(new StatusFilterOption(OrderStatus.ActiveFilter, "Active"));
        foreach (var status in OrderStatus.All)
        {
            StatusFilters.Add(new StatusFilterOption(status, OrderStatus.DisplayName(status)));
        }

        SelectedStatusFilter = StatusFilters[0];
        _suppressFilterReload = false;
        _live = new LiveListRefresh(LiveReloadAsync);
    }

    private readonly LiveListRefresh _live;
    private bool _silentLoad;

    /// <summary>Page hidden: stop live refresh (no reloads for an invisible list).</summary>
    public void Disappear() => _live.Detach();

    /// <summary>
    /// A background sync changed rows while the list is visible: reload quietly with the current search/filter,
    /// superseding any older load, rows kept on screen and updated in place. Typing wins: a pending search
    /// load reads the fresh rows anyway.
    /// </summary>
    public async Task LiveReloadAsync()
    {
        if (!HasSession || _searchDebounce.IsPending)
        {
            return;
        }

        Interlocked.Increment(ref _loadGeneration);
        IsBusy = false;
        await LoadCoreAsync(CancellationToken.None, silent: true);
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

    /// <summary>Spinner only when there is nothing to show yet; cached rows stay visible during a refresh.</summary>
    public bool ShowBusyIndicator => IsBusy && !IsRefreshing && !_silentLoad && Items.Count == 0;

    // What the current Items were loaded for; AppearAsync skips the reload when all still match.
    private string? _loadedBusinessId;
    private long _loadedVersion = -1;
    private DateOnly _loadedDay;
    private string? _loadedSearch;
    private string? _loadedStatus;

    /// <summary>
    /// Tab shown: keep cached rows when nothing changed since the last load (no local write, same business,
    /// filter and day — overdue depends on the day); otherwise reload in place without clearing first.
    /// </summary>
    public async Task AppearAsync()
    {
        _live.Attach();
        var businessId = await _auth.GetBusinessIdAsync();
        if (_loadedVersion >= 0 &&
            !string.IsNullOrWhiteSpace(businessId) &&
            string.Equals(businessId, _loadedBusinessId, StringComparison.Ordinal) &&
            LocalDataVersion.Current == _loadedVersion &&
            BusinessTime.Today(DateTimeOffset.UtcNow) == _loadedDay &&
            string.Equals(SearchText, _loadedSearch, StringComparison.Ordinal) &&
            string.Equals(SelectedStatusFilter?.Status, _loadedStatus, StringComparison.Ordinal) &&
            !HasError)
        {
            Perf.Log("orders.cacheHit", 0);
            return;
        }

        await LoadAsync();
    }

    /// <summary>Query key for deep links, e.g. <c>//main/OrdersPage?filter=Active</c>.</summary>
    public const string FilterQueryKey = "filter";

    /// <summary>
    /// Deep link from Home: preselect a filter (Active, Ready, All...). The tab's AppearAsync then loads it
    /// (filter differs from _loadedStatus) — no second load from the filter-changed handler. A plain tab tap
    /// carries no query, so the last visible filter stays (the picker shows it).
    /// </summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue(FilterQueryKey, out var raw))
        {
            return;
        }

        try { query.Remove(FilterQueryKey); } catch (NotSupportedException) { } // Shell can re-deliver a tab's last query; apply each link once
        ApplyFilter(Uri.UnescapeDataString(raw?.ToString() ?? string.Empty));
    }

    /// <summary>Select a filter by key without reloading here; unknown/empty keys mean All.</summary>
    public void ApplyFilter(string? key)
    {
        var option = StatusFilters.FirstOrDefault(o =>
            string.Equals(o.Status, string.IsNullOrWhiteSpace(key) || key == "All" ? null : key.Trim(), StringComparison.Ordinal))
            ?? StatusFilters[0];
        if (ReferenceEquals(option, SelectedStatusFilter))
        {
            return; // repeated taps: AppearAsync keeps the cached rows
        }

        // A load for the old filter may be running: supersede it so its rows can't land under the new filter.
        Interlocked.Increment(ref _loadGeneration);
        IsBusy = false;
        ForgetLoaded();
        _suppressFilterReload = true;
        SelectedStatusFilter = option;
        _suppressFilterReload = false;
        OnPropertyChanged(nameof(ShowEmptyAdd));
    }

    private void ForgetLoaded()
    {
        _loadedBusinessId = null;
        _loadedVersion = -1;
    }

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
        (!IsBusy || _silentLoad) && HasSession && !HasError && string.IsNullOrWhiteSpace(SearchText) &&
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

        if (value is null)
        {
            // The Picker briefly reports null while its ItemsSource binds: treat as "All", don't reload twice.
            _suppressFilterReload = true;
            SelectedStatusFilter = StatusFilters[0];
            _suppressFilterReload = false;
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () => await ReloadForFilterAsync());
    }

    /// <summary>
    /// Filter changed: supersede any in-flight load (it was for the old filter) instead of being dropped by the
    /// IsBusy guard, which would leave the old filter's rows on screen.
    /// </summary>
    public Task ReloadForFilterAsync()
    {
        Interlocked.Increment(ref _loadGeneration);
        IsBusy = false;
        return LoadAsync();
    }

    public void ClearSessionState()
    {
        _live.Detach();
        Interlocked.Increment(ref _loadGeneration);
        ForgetLoaded();
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

    private async Task LoadCoreAsync(CancellationToken cancellationToken, bool silent = false)
    {
        if (IsBusy)
        {
            return;
        }

        _silentLoad = silent; // set before IsBusy so the spinner/empty CTA never flash for a quiet reload

        var generation = _loadGeneration;
        var versionAtStart = LocalDataVersion.Current; // read before the query: a write during it forces the next reload
        var perf = System.Diagnostics.Stopwatch.StartNew();
        IsBusy = true;
        OnPropertyChanged(nameof(ShowEmptyAdd));
        if (!silent)
        {
            ErrorMessage = null;
        }

        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            Perf.Log("orders.businessId", perf.ElapsedMilliseconds);
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
            Perf.Log("orders.search", perf.ElapsedMilliseconds);
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

            CollectionSync.Apply(Items, list, (row, dto) => row.Item == dto, dto => new OrderListRow(dto));
            OnPropertyChanged(nameof(ShowBusyIndicator));

            Perf.Log("orders.itemsFilled", perf.ElapsedMilliseconds);
            _loadedBusinessId = businessId;
            _loadedVersion = versionAtStart;
            _loadedDay = BusinessTime.Today(DateTimeOffset.UtcNow);
            _loadedSearch = search;
            _loadedStatus = status;
            EmptyMessage = EmptyMessageFor(search, status);
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

            // Keep cached rows if any; show inline error with pull-to-retry (a quiet reload stays quiet).
            ForgetLoaded();
            if (silent)
            {
                return;
            }

            ErrorMessage = "Couldn't load orders. Pull to try again.";
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                _silentLoad = false;
                IsBusy = false;
                IsRefreshing = false;
                OnPropertyChanged(nameof(ShowEmptyAdd));
            }
        }
    }

    private readonly SyncService? _sync;

    public static string EmptyMessageFor(string? search, string? status) =>
        (string.IsNullOrWhiteSpace(search), status) switch
        {
            (true, null) => "Create your first order to track sewing work.",
            (true, OrderStatus.ActiveFilter) => "No active orders. Everything is collected or cancelled.",
            (true, OrderStatus.Ready) => "Nothing is ready to collect right now.",
            _ => "No orders match that filter."
        };

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

    /// <summary>Row sync badge tapped: open Sync details (the row itself still opens the record).</summary>
    [RelayCommand]
    private Task OpenSyncDetailsAsync() => Shell.Current.GoToAsync("SyncDetailsPage");
}
