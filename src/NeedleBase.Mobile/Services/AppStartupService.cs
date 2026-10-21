using NeedleBase.Mobile.Data.Services;

namespace NeedleBase.Mobile.Services;

/// <summary>
/// Cold-start auth/DB gate. Runs under the native Maui splash; no second in-app splash page.
/// </summary>
public sealed class AppStartupService
{
    private readonly IAuthService _authService;
    private readonly INavigationService _navigation;
    private readonly ILocalDatabaseService _localDatabase;
    private readonly AccessRevokedService _accessRevoked;
    private bool _started;

    public AppStartupService(
        IAuthService authService,
        INavigationService navigation,
        ILocalDatabaseService localDatabase,
        AccessRevokedService accessRevoked)
    {
        _authService = authService;
        _navigation = navigation;
        _localDatabase = localDatabase;
        _accessRevoked = accessRevoked;
    }

    public async Task RunOnceAsync(Page host)
    {
        if (_started)
        {
            return;
        }

        _started = true;

        while (true)
        {
            try
            {
                try
                {
                    await _localDatabase.MigrateAsync();
                }
                catch (Exception dbEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Local DB startup failed: {dbEx.Message}");
                    // Do not mask a database failure as a sign-in problem.
                    await host.DisplayAlert(
                        "Couldn't prepare local data",
                        "NeedleBase could not set up storage on this device. Close and reopen the app if this keeps happening.",
                        "Retry");
                    continue;
                }

                // A revoke seen earlier whose wipe didn't finish (app killed, storage error): finish it first.
                if (await _accessRevoked.HandleAsync(host))
                {
                    return;
                }

                if (!await _authService.HasStoredTokensAsync())
                {
                    await _navigation.ShowLoginAsync();
                    return;
                }

                // Soft validate: transient refresh failure keeps session (offline honesty).
                var refresh = await _authService.RefreshAsync();
                if (refresh.AccessRevoked && await _accessRevoked.HandleAsync(host))
                {
                    return;
                }

                if (!(refresh.Succeeded || refresh.IsTransientFailure))
                {
                    await _authService.ClearTokensAsync();
                    await _navigation.ShowLoginAsync();
                    return;
                }

                var businessId = await _authService.GetBusinessIdAsync();
                if (string.IsNullOrWhiteSpace(businessId))
                {
                    await _authService.ClearTokensAsync();
                    await _navigation.ShowLoginAsync();
                    return;
                }

                await _navigation.ShowMainAsync();
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Startup failed: {ex.Message}");

                var hasSession = await _authService.HasStoredTokensAsync();
                if (!hasSession)
                {
                    await host.DisplayAlert(
                        "Couldn't start",
                        "The app could not finish starting. Try again.",
                        "Retry");
                    continue;
                }

                // Tokens exist — retry; do not treat as logged out.
                await host.DisplayAlert(
                    "Startup Error",
                    "The app could not finish starting. Your session is still saved.",
                    "Retry");
            }
        }
    }
}
