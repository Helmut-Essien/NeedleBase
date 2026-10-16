using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly IAuthService _authService;
    private readonly INavigationService _navigation;

    public LoginViewModel(IAuthService authService, INavigationService navigation)
    {
        _authService = authService;
        _navigation = navigation;
        Title = "Sign in";
    }

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Enter email and password.";
            return;
        }

        IsBusy = true;
        try
        {
            var user = await _authService.LoginAsync(new LoginRequest(Email.Trim(), Password));
            if (user is null)
            {
                ErrorMessage = "Invalid email or password.";
                return;
            }

            await _navigation.ShowMainAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = "Couldn’t sign in. Check your connection and try again.";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
