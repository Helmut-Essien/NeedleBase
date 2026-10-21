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
    }

    public ObservableCollection<CustomerListItemDto> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyAdd))]
    private string searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBusyIndicator))]
    private bool isRefreshing;

    public bool ShowBusyIndicator => IsBusy && !IsRefreshing;

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
        !IsBusy && HasSession && string.IsNullOrWhiteSpace(SearchText);

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
        Interlocked.Increment(ref _loadGeneration);
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
                EmptyMessage = "Sign in to see customers on this device.";
                ErrorMessage = "Missing business session.";
                return;
            }

            var search = SearchText;
            var list = await _customers.SearchAsync(businessId!, search, cancellationToken: cancellationToken);
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

            Items.Clear();
            foreach (var item in list)
            {
                Items.Add(item);
            }

            EmptyMessage = string.IsNullOrWhiteSpace(search)
                ? "Add your first customer to get started."
                : $"No customers match \"{search.Trim()}\".";
            OnPropertyChanged(nameof(HasItems));
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

            Items.Clear();
            OnPropertyChanged(nameof(HasItems));
            ErrorMessage = "Couldn't load customers. Pull to try again.";
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
}
