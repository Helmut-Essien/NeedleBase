using CommunityToolkit.Mvvm.ComponentModel;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

public partial class SplashViewModel : BaseViewModel
{
    private readonly IAuthService _authService;
    private readonly INavigationService _navigation;

    public SplashViewModel(IAuthService authService, INavigationService navigation)
    {
        _authService = authService;
        _navigation = navigation;
        Title = "NeedleBase";
        StatusMessage = "Starting…";
    }

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public async Task InitializeAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (!await _authService.HasStoredTokensAsync())
            {
                await _navigation.ShowLoginAsync();
                return;
            }

            // Soft validate: transient refresh failure keeps session (offline honesty).
            StatusMessage = "Checking session…";
            var refresh = await _authService.RefreshAsync();
            if (refresh.Succeeded || refresh.IsTransientFailure)
            {
                await _navigation.ShowMainAsync();
                return;
            }

            // Invalid session only
            await _authService.ClearTokensAsync();
            await _navigation.ShowLoginAsync();
        }
        catch
        {
            // Startup/storage glitch with tokens present → retry main, do not force logout.
            if (await _authService.HasStoredTokensAsync())
            {
                await _navigation.ShowMainAsync();
            }
            else
            {
                await _navigation.ShowLoginAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
