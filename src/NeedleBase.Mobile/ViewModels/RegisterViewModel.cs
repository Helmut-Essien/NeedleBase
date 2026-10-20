using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Mobile.Messaging;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

public partial class RegisterViewModel : BaseViewModel
{
    private readonly IAuthService _authService;
    private readonly INavigationService _navigation;

    public RegisterViewModel(IAuthService authService, INavigationService navigation)
    {
        _authService = authService;
        _navigation = navigation;
        Title = "Register business";

        WeakReferenceMessenger.Default.Register<ClearAuthSensitiveStateMessage>(
            this,
            static (recipient, _) => ((RegisterViewModel)recipient).ClearSensitiveFields());
    }

    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string confirmPassword = string.Empty;
    [ObservableProperty] private string firstName = string.Empty;
    [ObservableProperty] private string surname = string.Empty;
    [ObservableProperty] private string businessName = string.Empty;
    [ObservableProperty] private string? businessPhone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPasswordHidden))]
    [NotifyPropertyChangedFor(nameof(PasswordToggleDescription))]
    private bool isPasswordVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmPasswordHidden))]
    [NotifyPropertyChangedFor(nameof(ConfirmPasswordToggleDescription))]
    private bool isConfirmPasswordVisible;

    public bool IsPasswordHidden => !IsPasswordVisible;
    public bool IsConfirmPasswordHidden => !IsConfirmPasswordVisible;

    public string PasswordToggleDescription =>
        IsPasswordVisible ? "Hide password" : "Show password";

    public string ConfirmPasswordToggleDescription =>
        IsConfirmPasswordVisible ? "Hide confirm password" : "Show confirm password";

    // Client-side gate only: not sent to the Api and not part of any shared DTO.
    [ObservableProperty] private bool acceptTerms;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTermsError))]
    private string? termsError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPasswordError))]
    private string? passwordError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfirmPasswordError))]
    private string? confirmPasswordError;

    public bool HasTermsError => !string.IsNullOrWhiteSpace(TermsError);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasPasswordError => !string.IsNullOrWhiteSpace(PasswordError);
    public bool HasConfirmPasswordError => !string.IsNullOrWhiteSpace(ConfirmPasswordError);

    partial void OnAcceptTermsChanged(bool value)
    {
        if (value)
        {
            TermsError = null;
        }
    }

    partial void OnPasswordChanged(string value)
    {
        PasswordError = null;
        ConfirmPasswordError = null;
    }

    partial void OnConfirmPasswordChanged(string value)
    {
        ConfirmPasswordError = null;
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private void TogglePasswordVisibility()
    {
        IsPasswordVisible = !IsPasswordVisible;
        AnnouncePasswordToggle(PasswordToggleDescription);
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private void ToggleConfirmPasswordVisibility()
    {
        IsConfirmPasswordVisible = !IsConfirmPasswordVisible;
        AnnouncePasswordToggle(ConfirmPasswordToggleDescription);
    }

    protected override void OnBusyChanged()
    {
        TogglePasswordVisibilityCommand.NotifyCanExecuteChanged();
        ToggleConfirmPasswordVisibilityCommand.NotifyCanExecuteChanged();
    }

    private static void AnnouncePasswordToggle(string message)
    {
        try
        {
            SemanticScreenReader.Default.Announce(message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    /// <summary>Remask on appear (Terms/Privacy / resume). Secrets clear via logout message or LeaveToLogin.</summary>
    public void PrepareForDisplay() => RemaskPasswords();

    /// <summary>Hide plaintext if the page leaves the foreground (nav, app switcher).</summary>
    public void RemaskPasswords()
    {
        IsPasswordVisible = false;
        IsConfirmPasswordVisible = false;
    }

    public void ClearSensitiveFields()
    {
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        RemaskPasswords();
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        TermsError = null;
        PasswordError = null;
        ConfirmPasswordError = null;
        if (string.IsNullOrWhiteSpace(Email) ||
            string.IsNullOrWhiteSpace(Password) ||
            string.IsNullOrWhiteSpace(ConfirmPassword) ||
            string.IsNullOrWhiteSpace(FirstName) ||
            string.IsNullOrWhiteSpace(Surname) ||
            string.IsNullOrWhiteSpace(BusinessName))
        {
            ErrorMessage = "Fill in all required fields.";
            return;
        }

        if (Password.Length < 6 || !Password.Any(char.IsDigit))
        {
            PasswordError = "At least 6 characters, including a number";
            return;
        }

        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
        {
            ConfirmPasswordError = "Passwords do not match";
            return;
        }

        if (!AcceptTerms)
        {
            TermsError = "Please agree to the Terms and Conditions before creating an account.";
            return;
        }

        IsPasswordVisible = false;
        IsConfirmPasswordVisible = false;
        IsBusy = true;
        try
        {
            var request = new RegisterBusinessRequest(
                Email.Trim(),
                Password,
                FirstName.Trim(),
                Surname.Trim(),
                BusinessName.Trim(),
                string.IsNullOrWhiteSpace(BusinessPhone) ? null : BusinessPhone.Trim());

            var user = await _authService.RegisterBusinessAsync(request);
            if (user is null)
            {
                ErrorMessage = _authService.LastErrorMessage
                    ?? "Couldn’t register with those details. Try a different email or check your connection.";
                return;
            }

            ClearSensitiveFields();
            await _navigation.ShowMainAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = "Couldn’t register. Check your connection and try again.";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
