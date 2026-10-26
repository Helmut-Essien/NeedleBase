using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>Change password (online). Other phones/browsers get signed out; this phone keeps going.</summary>
public partial class ChangePasswordViewModel : BaseViewModel
{
    private readonly IAuthService _auth;

    public ChangePasswordViewModel(IAuthService auth)
    {
        _auth = auth;
        Title = "Change password";
    }

    [ObservableProperty] private string currentPassword = string.Empty;
    [ObservableProperty] private string newPassword = string.Empty;
    [ObservableProperty] private string confirmPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? statusMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    public string Hint => PasswordRules.Hint;

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        StatusMessage = null;
        if (string.IsNullOrEmpty(CurrentPassword))
        {
            ErrorMessage = "Enter your current password.";
            return;
        }

        if (!PasswordRules.IsStrongEnough(NewPassword))
        {
            ErrorMessage = PasswordRules.Hint + ".";
            return;
        }

        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "The new passwords don't match.";
            return;
        }

        if (string.Equals(NewPassword, CurrentPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "Pick a password different from the current one.";
            return;
        }

        IsBusy = true;
        try
        {
            var ok = await _auth.ChangePasswordAsync(new ChangePasswordRequest { CurrentPassword = CurrentPassword, NewPassword = NewPassword });
            if (!ok)
            {
                ErrorMessage = _auth.LastErrorMessage ?? "Couldn't change your password. Try again.";
                return;
            }

            ErrorMessage = null;
            Clear();
            StatusMessage = "Password changed. Other phones and browsers have been signed out.";
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Can't reach NeedleBase. Check your connection and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Clear()
    {
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
    }
}
