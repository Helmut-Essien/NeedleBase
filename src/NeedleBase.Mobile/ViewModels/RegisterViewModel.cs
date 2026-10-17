using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Contracts.Auth;
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
    }

    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string confirmPassword = string.Empty;
    [ObservableProperty] private string firstName = string.Empty;
    [ObservableProperty] private string surname = string.Empty;
    [ObservableProperty] private string businessName = string.Empty;
    [ObservableProperty] private string? businessPhone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPasswordError))]
    private string? passwordError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfirmPasswordError))]
    private string? confirmPasswordError;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasPasswordError => !string.IsNullOrWhiteSpace(PasswordError);
    public bool HasConfirmPasswordError => !string.IsNullOrWhiteSpace(ConfirmPasswordError);

    partial void OnPasswordChanged(string value)
    {
        PasswordError = null;
        ConfirmPasswordError = null;
    }

    partial void OnConfirmPasswordChanged(string value)
    {
        ConfirmPasswordError = null;
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
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
                ErrorMessage = "Couldn’t register with those details. Try a different email or check your connection.";
                return;
            }

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
