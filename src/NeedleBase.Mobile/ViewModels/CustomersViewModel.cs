using NeedleBase.Mobile.Data.Data;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Helpers;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

public partial class CustomersViewModel : BaseViewModel
{
    private readonly ICustomerLocalService _customers;
    private readonly IAuthService _auth;
    private int _loadGeneration;
    private readonly SearchDebouncer _searchDebounce = new();

    public CustomersViewModel(ICustomerLocalService customers, IAuthService auth, SyncService? sync = null)
    {
        _sync = sync;
        _customers = customers;
        _auth = auth;
        Title = "Customers";
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

    public ObservableCollection<CustomerListItemDto> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyAdd))]
    private string searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBusyIndicator))]
    private bool isRefreshing;

    /// <summary>Spinner only when there is nothing to show yet; cached rows stay visible during a refresh.</summary>
    public bool ShowBusyIndicator => IsBusy && !IsRefreshing && !_silentLoad && Items.Count == 0;

    // What the current Items were loaded for; AppearAsync skips the reload when all still match.
    private string? _loadedBusinessId;
    private long _loadedVersion = -1;
    private string? _loadedSearch;

    /// <summary>
    /// Tab shown: keep cached rows when nothing changed since the last load (no local write — saves, sync
    /// applies, bulk deletes, wipe all bump <see cref="LocalDataVersion"/> — same business and search).
    /// </summary>
    public async Task AppearAsync()
    {
        _live.Attach();
        var businessId = await _auth.GetBusinessIdAsync();
        if (_loadedVersion >= 0 &&
            !string.IsNullOrWhiteSpace(businessId) &&
            string.Equals(businessId, _loadedBusinessId, StringComparison.Ordinal) &&
            LocalDataVersion.Current == _loadedVersion &&
            string.Equals(SearchText, _loadedSearch, StringComparison.Ordinal) &&
            !HasError)
        {
            Perf.Log("customers.cacheHit", 0);
            return;
        }

        await LoadAsync();
    }

    private void ForgetLoaded()
    {
        _loadedBusinessId = null;
        _loadedVersion = -1;
    }

    [ObservableProperty]
    private string emptyMessage = "Add your first customer to get started.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyAdd))]
    private string? errorMessage;

    [ObservableProperty]
    private CustomerListItemDto? selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyAdd))]
    private bool hasSession;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasItems => Items.Count > 0;

    /// <summary>Notify empty CTA + center spinner when IsBusy changes (pull-to-refresh uses RefreshView only).</summary>
    protected override void OnBusyChanged()
    {
        OnPropertyChanged(nameof(ShowEmptyAdd));
        OnPropertyChanged(nameof(ShowBusyIndicator));
    }

    public bool ShowEmptyAdd =>
        (!IsBusy || _silentLoad) && HasSession && string.IsNullOrWhiteSpace(SearchText);

    partial void OnSelectedItemChanged(CustomerListItemDto? value)
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

    /// <summary>Clear in-memory list/search so the next session cannot flash prior tenant PII.</summary>
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
        EmptyMessage = "Add your first customer to get started.";
        OnPropertyChanged(nameof(HasItems));
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
            if (generation != _loadGeneration)
            {
                return;
            }

            HasSession = !string.IsNullOrWhiteSpace(businessId);
            if (!HasSession)
            {
                ClearSessionState();
                EmptyMessage = "Sign in to see customers on this device.";
                ErrorMessage = "Missing business session.";
                return;
            }

            var search = SearchText;
            Perf.Log("customers.businessId", perf.ElapsedMilliseconds);
            var list = await Task.Run( // query translation off the UI thread
                () => _customers.SearchAsync(businessId!, search, cancellationToken: cancellationToken),
                cancellationToken);
            Perf.Log("customers.search", perf.ElapsedMilliseconds);
            if (generation != _loadGeneration)
            {
                return;
            }

            // Drop results if session changed mid-flight (logout/login).
            var currentBusinessId = await _auth.GetBusinessIdAsync();
            if (generation != _loadGeneration ||
                !string.Equals(currentBusinessId, businessId, StringComparison.Ordinal))
            {
                return;
            }

            CollectionSync.Apply(Items, list, (row, dto) => row == dto, dto => dto);
            OnPropertyChanged(nameof(ShowBusyIndicator));

            EmptyMessage = string.IsNullOrWhiteSpace(search)
                ? "Add your first customer to get started."
                : $"No customers match \"{search.Trim()}\".";
            OnPropertyChanged(nameof(HasItems));
            _loadedBusinessId = businessId;
            _loadedVersion = versionAtStart;
            _loadedSearch = search;
            Perf.Log("customers.itemsFilled", perf.ElapsedMilliseconds);
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

            ForgetLoaded();
            if (silent)
            {
                return; // a quiet reload keeps the rows on screen
            }

            Items.Clear();
            OnPropertyChanged(nameof(HasItems));
            ErrorMessage = "Couldn't load customers. Pull to try again.";
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
    private async Task AddAsync() =>
        await Shell.Current.GoToAsync("CustomerEditPage");

    [RelayCommand]
    private async Task OpenAsync(CustomerListItemDto? item)
    {
        if (item is null)
        {
            return;
        }

        await Shell.Current.GoToAsync($"CustomerDetailPage?customerId={Uri.EscapeDataString(item.Id)}");
    }

    /// <summary>Row sync badge tapped: open Sync details (the row itself still opens the record).</summary>
    [RelayCommand]
    private Task OpenSyncDetailsAsync() => Shell.Current.GoToAsync("SyncDetailsPage");
}
