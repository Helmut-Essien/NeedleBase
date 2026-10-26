using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>
/// Forgot password (online, only when the server can send email): ask for a code by email, then
/// enter the code with a new password. The reply never says whether an account exists.
/// </summary>
public partial class ForgotPasswordViewModel : BaseViewModel
{
    private readonly IAuthService _auth;

    public ForgotPasswordViewModel(IAuthService auth)
    {
        _auth = auth;
        Title = "Reset password";
    }

    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string code = string.Empty;
    [ObservableProperty] private string newPassword = string.Empty;
    [ObservableProperty] private string confirmPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsEmail))]
    private bool codeSent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsEmail))]
    [NotifyPropertyChangedFor(nameof(CanEnterCode))]
    private bool done;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? statusMessage;

    public bool NeedsEmail => !CodeSent && !Done;
    public bool CanEnterCode => CodeSent && !Done;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    public string Hint => PasswordRules.Hint;

    partial void OnCodeSentChanged(bool value) => OnPropertyChanged(nameof(CanEnterCode));

    [RelayCommand]
    public async Task SendCodeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var address = Email.Trim();
        if (!StaffViewModel.IsEmail(address))
        {
            ErrorMessage = "Enter the email you sign in with.";
            return;
        }

        IsBusy = true;
        try
        {
            if (!await _auth.RequestPasswordResetAsync(address))
            {
                ErrorMessage = _auth.LastErrorMessage ?? "Couldn't send the code. Try again.";
                return;
            }

            Email = address;
            ErrorMessage = null;
            CodeSent = true;
            StatusMessage = $"If {address} has a NeedleBase account, we've emailed an 8-digit code. It works for 30 minutes. Check spam too.";
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Can't reach NeedleBase. Check your connection and try again.";
        }
        catch (TaskCanceledException)
        {
            ErrorMessage = "Can't reach NeedleBase. Check your connection and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ResetAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var digits = new string(Code.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != 8)
        {
            ErrorMessage = "Enter the 8-digit code from the email.";
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

        IsBusy = true;
        try
        {
            if (!await _auth.ResetPasswordAsync(new ResetPasswordRequest { Email = Email.Trim(), Code = digits, NewPassword = NewPassword }))
            {
                ErrorMessage = _auth.LastErrorMessage ?? "Couldn't reset your password. Try again.";
                return;
            }

            ErrorMessage = null;
            ClearSecrets();
            Done = true;
            StatusMessage = "Password changed. Sign in with your new password. Other phones and browsers have been signed out.";
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Can't reach NeedleBase. Check your connection and try again.";
        }
        catch (TaskCanceledException)
        {
            ErrorMessage = "Can't reach NeedleBase. Check your connection and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void UseDifferentEmail()
    {
        ClearSecrets();
        CodeSent = false;
        ErrorMessage = null;
        StatusMessage = null;
    }

    private void ClearSecrets()
    {
        Code = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
    }

    /// <summary>Called when leaving the screen: nothing sensitive stays in memory.</summary>
    public void Reset()
    {
        ClearSecrets();
        CodeSent = false;
        Done = false;
        ErrorMessage = null;
        StatusMessage = null;
    }
}
