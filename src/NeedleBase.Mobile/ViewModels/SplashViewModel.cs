using CommunityToolkit.Mvvm.ComponentModel;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

public partial class SplashViewModel : BaseViewModel
{
    private readonly IAuthService _authService;
    private readonly INavigationService _navigation;
    private readonly ILocalDatabaseService _localDatabase;

    public SplashViewModel(
        IAuthService authService,
        INavigationService navigation,
        ILocalDatabaseService localDatabase)
    {
        _authService = authService;
        _navigation = navigation;
        _localDatabase = localDatabase;
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
            StatusMessage = "Preparing local data…";
            try
            {
                await _localDatabase.EnsureCreatedAsync();
            }
            catch
            {
                StatusMessage = "Couldn't prepare local data. Close and reopen the app.";
                return;
            }

            if (!await _authService.HasStoredTokensAsync())
            {
                await _navigation.ShowLoginAsync();
                return;
            }

            // Soft validate: transient refresh failure keeps session (offline honesty).
            StatusMessage = "Checking session…";
            var refresh = await _authService.RefreshAsync();
            if (!(refresh.Succeeded || refresh.IsTransientFailure))
            {
                await _authService.ClearTokensAsync();
                await _navigation.ShowLoginAsync();
                return;
            }

            var businessId = await _authService.GetBusinessIdAsync();
            if (string.IsNullOrWhiteSpace(businessId))
            {
                // Tokens without tenant context — cannot scope local data safely.
                StatusMessage = "Session is missing business context. Please sign in again.";
                await _authService.ClearTokensAsync();
                await _navigation.ShowLoginAsync();
                return;
            }

            await _navigation.ShowMainAsync();
        }
        catch
        {
            StatusMessage = "Couldn't start. Please try again.";
            if (await _authService.HasStoredTokensAsync() &&
                !string.IsNullOrWhiteSpace(await _authService.GetBusinessIdAsync()))
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
