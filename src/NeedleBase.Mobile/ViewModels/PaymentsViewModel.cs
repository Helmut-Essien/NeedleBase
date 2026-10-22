using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Payments;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Helpers;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>
/// Payments tab: newest payments on this device (cached list + live refresh, like Orders). Owner/Manager only;
/// a tailor sees why it's empty. Recording starts from an order (the payment needs its balance).
/// </summary>
public partial class PaymentsViewModel : BaseViewModel
{
    private readonly IPaymentLocalService _payments;
    private readonly IAuthService _auth;
    private readonly LiveListRefresh _live;
    private int _loadGeneration;
    private string? _loadedBusinessId;
    private long _loadedVersion = -1;
    private string? _loadedRole;

    /// <summary>The tab shows the newest rows only; each order shows all of its payments.</summary>
    public const int ListLimit = 100;

    public PaymentsViewModel(IPaymentLocalService payments, IAuthService auth)
    {
        _payments = payments;
        _auth = auth;
        Title = "Payments";
        _live = new LiveListRefresh(LiveReloadAsync);
    }

    public ObservableCollection<LocalPaymentListItem> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private bool canManage = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private string? errorMessage;

    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private LocalPaymentListItem? selectedItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private bool loaded;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool ShowEmpty => Loaded && CanManage && !HasError && Items.Count == 0;

    public bool ShowLimitNote => Loaded && CanManage && Items.Count >= ListLimit;

    public string LimitNote => $"Showing the latest {ListLimit} payments. Open an order to see all of its payments.";

    public string EmptyMessage => "No payments yet. Open an order and tap Record payment.";

    public async Task AppearAsync()
    {
        _live.Attach();
        var businessId = await _auth.GetBusinessIdAsync();
        var role = (await _auth.GetCachedUserAsync())?.Role;
        if (_loadedVersion >= 0 && LocalDataVersion.Current == _loadedVersion &&
            string.Equals(businessId, _loadedBusinessId, StringComparison.Ordinal) &&
            string.Equals(role, _loadedRole, StringComparison.Ordinal) && !HasError)
        {
            return; // nothing written since the last load
        }

        await LoadAsync();
    }

    public void Disappear() => _live.Detach();

    public Task LiveReloadAsync()
    {
        Interlocked.Increment(ref _loadGeneration);
        IsBusy = false;
        return LoadCoreAsync(silent: true);
    }

    public void ClearSessionState()
    {
        _live.Detach();
        Interlocked.Increment(ref _loadGeneration);
        _loadedVersion = -1;
        _loadedBusinessId = null;
        _loadedRole = null;
        Items.Clear();
        ErrorMessage = null;
        Loaded = false;
        IsBusy = false;
    }

    [RelayCommand]
    private Task LoadAsync() => LoadCoreAsync(silent: false);

    private async Task LoadCoreAsync(bool silent)
    {
        if (IsBusy)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _loadGeneration);
        var version = LocalDataVersion.Current;
        IsBusy = true;
        if (!silent)
        {
            ErrorMessage = null;
        }

        try
        {
            var businessId = await _auth.GetBusinessIdAsync();
            var user = await _auth.GetCachedUserAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                return;
            }

            CanManage = PaymentRules.CanManage(user?.Role);
            if (!CanManage)
            {
                Items.Clear();
                ErrorMessage = "Payments are only shown to the owner and managers.";
                _loadedRole = user?.Role;
                OnPropertyChanged(nameof(ShowLimitNote));
                // Role changed to Tailor on this phone: drop synced payment copies (queued ones stay; they're real money).
                await _payments.PurgeHiddenAsync(businessId);
                return;
            }

            var rows = await _payments.ListAsync(businessId, ListLimit);
            if (generation != _loadGeneration)
            {
                return; // superseded
            }

            CollectionSync.Apply(Items, rows, (a, b) => a == b, r => r);
            _loadedBusinessId = businessId;
            _loadedVersion = version;
            _loadedRole = user?.Role;
            Loaded = true;
            OnPropertyChanged(nameof(ShowEmpty));
            OnPropertyChanged(nameof(ShowLimitNote));
        }
        catch (Exception)
        {
            if (!silent)
            {
                ErrorMessage = "Couldn't load payments.";
            }
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                IsBusy = false;
                IsRefreshing = false;
            }
        }
    }

    partial void OnSelectedItemChanged(LocalPaymentListItem? value)
    {
        if (value is null)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Shell.Current.GoToAsync($"PaymentReceiptPage?paymentId={Uri.EscapeDataString(value.Id)}");
            SelectedItem = null;
        });
    }

    [RelayCommand]
    private async Task GoToOrdersAsync() => await Shell.Current.GoToAsync("//main/OrdersPage");
}
