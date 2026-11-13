using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>Delete the signed-in account (staff) or close the shop (owner). Online; wipes this device on success.</summary>
public partial class DeleteAccountViewModel : BaseViewModel
{
    private readonly IAuthService _auth;
    private readonly INavigationService _navigation;
    private readonly SyncService _sync;

    public DeleteAccountViewModel(IAuthService auth, INavigationService navigation, SyncService sync)
    {
        _auth = auth;
        _navigation = navigation;
        _sync = sync;
        Title = "Delete account";
    }

    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string confirmation = string.Empty;
    [ObservableProperty] private bool isOwner;
    [ObservableProperty] private string warning = string.Empty;
    [ObservableProperty] private string confirmationLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string ExpectedConfirmation => IsOwner ? DeleteAccountConfirmations.OwnerShop : DeleteAccountConfirmations.Staff;

    public async Task LoadAsync()
    {
        var user = await _auth.GetCachedUserAsync();
        IsOwner = string.Equals(user?.Role, BusinessRole.Owner, StringComparison.OrdinalIgnoreCase);
        ConfirmationLabel = $"Type {ExpectedConfirmation} to confirm";
        Warning = IsOwner
            ? "You are the shop owner. Deleting your account closes the whole shop. You and your staff will be signed out and will not be able to sign in again. The shop's records on our hosted service (customers, measurements, orders, payments and WhatsApp settings) will be deleted at once. The activity log is kept for up to two years. A copy only on this phone is cleared when deletion succeeds."
            : "This removes your sign-in only. The shop's customer, order and payment records stay, and other people in the shop can still use NeedleBase. This cannot be undone.";
    }

    [RelayCommand]
    public async Task DeleteAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Enter your password.";
            return;
        }

        if (!DeleteAccountConfirmations.Matches(Confirmation, ExpectedConfirmation))
        {
            ErrorMessage = $"Type {ExpectedConfirmation} exactly to confirm.";
            return;
        }

        IsBusy = true;
        var stoppedSync = false;
        var leaveToLogin = false;
        try
        {
            await _sync.StopAsync();
            stoppedSync = true;
            var ok = await _auth.DeleteAccountAsync(new DeleteAccountRequest
            {
                Password = Password,
                Confirmation = Confirmation
            });
            if (!ok)
            {
                ErrorMessage = _auth.LastErrorMessage ?? "Couldn't delete the account. Try again.";
                return;
            }

            ErrorMessage = null;
            Clear();
            leaveToLogin = true;
            await _navigation.ShowLoginAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ErrorMessage = "Can't reach NeedleBase. Check your connection and try again.";
        }
        finally
        {
            IsBusy = false;
            if (stoppedSync && !leaveToLogin)
            {
                _sync.Start();
            }
        }
    }

    public void Clear()
    {
        Password = string.Empty;
        Confirmation = string.Empty;
    }
}
