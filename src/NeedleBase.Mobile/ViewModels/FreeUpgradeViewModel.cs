using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Mobile.Messaging;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>Free → online: register with a licence key and keep records already on this phone.</summary>
public partial class FreeUpgradeViewModel : BaseViewModel
{
    private readonly IAuthService _authService;
    private readonly INavigationService _navigation;

    public FreeUpgradeViewModel(IAuthService authService, INavigationService navigation)
    {
        _authService = authService;
        _navigation = navigation;
        Title = "Go online";

        WeakReferenceMessenger.Default.Register<ClearAuthSensitiveStateMessage>(
            this,
            static (recipient, _) => ((FreeUpgradeViewModel)recipient).ClearSensitiveFields());
    }

    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string confirmPassword = string.Empty;
    [ObservableProperty] private string firstName = string.Empty;
    [ObservableProperty] private string surname = string.Empty;
    [ObservableProperty] private string businessName = string.Empty;
    [ObservableProperty] private string? businessPhone;
    [ObservableProperty] private string? licenseKey;

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

    public async Task PrepareAsync()
    {
        if (string.IsNullOrWhiteSpace(BusinessName))
        {
            var user = await _authService.GetCachedUserAsync();
            if (!string.IsNullOrWhiteSpace(user?.BusinessName))
            {
                BusinessName = user.BusinessName;
            }
        }
    }

    public void ClearSensitiveFields()
    {
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        LicenseKey = null;
    }

    [RelayCommand]
    private async Task UpgradeAsync()
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
            string.IsNullOrWhiteSpace(BusinessName) ||
            string.IsNullOrWhiteSpace(LicenseKey))
        {
            ErrorMessage = "Fill in all required fields, including your licence key.";
            return;
        }

        if (!PasswordRules.IsStrongEnough(Password))
        {
            PasswordError = PasswordRules.Hint;
            return;
        }

        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
        {
            ConfirmPasswordError = "Passwords do not match";
            return;
        }

        if (!AcceptTerms)
        {
            TermsError = "Please agree to the Terms and Conditions before going online.";
            return;
        }

        IsBusy = true;
        try
        {
            var request = new RegisterBusinessRequest(
                Email.Trim(),
                Password,
                FirstName.Trim(),
                Surname.Trim(),
                BusinessName.Trim(),
                string.IsNullOrWhiteSpace(BusinessPhone) ? null : BusinessPhone.Trim(),
                LicenseKey.Trim());

            var user = await _authService.UpgradeFromFreeAsync(request);
            if (user is null)
            {
                ErrorMessage = _authService.LastErrorMessage
                    ?? "Couldn't go online with those details. Check your licence key and connection.";
                return;
            }

            ClearSensitiveFields();
            await _navigation.ShowMainAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = "Couldn't go online. Check your connection and try again.";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task BackAsync() => Shell.Current.GoToAsync("..");
}
