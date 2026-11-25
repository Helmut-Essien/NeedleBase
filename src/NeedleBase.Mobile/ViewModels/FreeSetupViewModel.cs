using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

/// <summary>Name the Free shop on this phone (no account, no licence key).</summary>
public partial class FreeSetupViewModel : BaseViewModel
{
    private readonly IAuthService _authService;
    private readonly INavigationService _navigation;

    public FreeSetupViewModel(IAuthService authService, INavigationService navigation)
    {
        _authService = authService;
        _authService.OtherAccountDataPrompt ??= AuthPrompts.OtherAccountAsync;
        _navigation = navigation;
        Title = "Free on this phone";
    }

    [ObservableProperty]
    private string shopName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(ShopName))
        {
            ErrorMessage = "Enter your shop name.";
            return;
        }

        IsBusy = true;
        try
        {
            var user = await _authService.StartFreeModeAsync(ShopName);
            if (user is null)
            {
                ErrorMessage = _authService.LastErrorMessage ?? "Couldn't start Free on this phone.";
                return;
            }

            await _navigation.ShowMainAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = "Couldn't start Free on this phone. Try again.";
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
