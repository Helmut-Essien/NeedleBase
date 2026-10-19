using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Mobile.Messaging;
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

        WeakReferenceMessenger.Default.Register<ClearAuthSensitiveStateMessage>(
            this,
            static (recipient, _) => ((LoginViewModel)recipient).ClearSensitiveFields());
    }

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPasswordHidden))]
    [NotifyPropertyChangedFor(nameof(PasswordToggleDescription))]
    private bool isPasswordVisible;

    public bool IsPasswordHidden => !IsPasswordVisible;

    public string PasswordToggleDescription =>
        IsPasswordVisible ? "Hide password" : "Show password";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private void TogglePasswordVisibility()
    {
        IsPasswordVisible = !IsPasswordVisible;
        AnnouncePasswordToggle(PasswordToggleDescription);
    }

    protected override void OnBusyChanged()
    {
        TogglePasswordVisibilityCommand.NotifyCanExecuteChanged();
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

    /// <summary>Remask on appear/resume. Secrets clear via logout message.</summary>
    public void PrepareForDisplay() => RemaskPassword();

    /// <summary>Hide plaintext if the page leaves the foreground (nav, app switcher).</summary>
    public void RemaskPassword() => IsPasswordVisible = false;

    public void ClearSensitiveFields()
    {
        Password = string.Empty;
        IsPasswordVisible = false;
        ErrorMessage = null;
    }

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

        IsPasswordVisible = false;
        IsBusy = true;
        try
        {
            var user = await _authService.LoginAsync(new LoginRequest(Email.Trim(), Password));
            if (user is null)
            {
                ErrorMessage = "Invalid email or password.";
                return;
            }

            ClearSensitiveFields();
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
