using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;

namespace NeedleBase.Mobile.Services;

/// <summary>
/// Cold-start auth/DB gate. Runs under the native Maui splash; no second in-app splash page.
/// </summary>
public sealed class AppStartupService
{
    private readonly IAuthService _authService;
    private readonly INavigationService _navigation;
    private readonly ILocalDatabaseService _localDatabase;
    private readonly IFreeModeLocalService _freeMode;
    private readonly AccessRevokedService _accessRevoked;
    private readonly SubscriptionStatusStore? _subscription;
    private bool _started;

    public AppStartupService(
        IAuthService authService,
        INavigationService navigation,
        ILocalDatabaseService localDatabase,
        IFreeModeLocalService freeMode,
        AccessRevokedService accessRevoked,
        SubscriptionStatusStore? subscription = null)
    {
        _subscription = subscription;
        _authService = authService;
        _navigation = navigation;
        _localDatabase = localDatabase;
        _freeMode = freeMode;
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
                    var startupPerf = System.Diagnostics.Stopwatch.StartNew();
                    await _localDatabase.MigrateAsync();
                    NeedleBase.Mobile.Helpers.Perf.Log("startup.migrate", startupPerf.ElapsedMilliseconds);
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

                // Free (phone only): no tokens, no refresh — restore the in-memory gate and open the app.
                if (await _authService.IsFreeModeAsync())
                {
                    var freeBusinessId = await _authService.GetBusinessIdAsync();
                    if (string.IsNullOrWhiteSpace(freeBusinessId))
                    {
                        await _authService.ClearTokensAsync();
                        await _navigation.ShowLoginAsync();
                        return;
                    }

                    // Caps need a FreeModeStates row — recreate from the stored shop name if it was lost.
                    if (!await _freeMode.ExistsAsync(freeBusinessId))
                    {
                        var shopName = (await _authService.GetCachedUserAsync())?.BusinessName;
                        if (string.IsNullOrWhiteSpace(shopName))
                        {
                            shopName = "Shop";
                        }

                        await _freeMode.CreateAsync(freeBusinessId, shopName, DateTimeOffset.UtcNow);
                    }

                    LocalFreeModeGate.Set(freeBusinessId);
                    await _navigation.ShowMainAsync();
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

                if (_subscription is not null)
                {
                    await _subscription.RestoreAsync(businessId); // banner + pause until the next sync answers
                }

                await _navigation.ShowMainAsync();
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Startup failed: {ex.Message}");

                var hasSession = await _authService.HasStoredTokensAsync() || await _authService.IsFreeModeAsync();
                if (!hasSession)
                {
                    await host.DisplayAlert(
                        "Couldn't start",
                        "The app could not finish starting. Try again.",
                        "Retry");
                    continue;
                }

                // Session exists — retry; do not treat as logged out.
                await host.DisplayAlert(
                    "Startup Error",
                    "The app could not finish starting. Your session is still saved.",
                    "Retry");
            }
        }
    }
}
