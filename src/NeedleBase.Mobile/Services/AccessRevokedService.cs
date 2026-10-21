using NeedleBase.Mobile.Data.Services;

namespace NeedleBase.Mobile.Services;

/// <summary>
/// The server said this account is deactivated or no longer a member of the business (refresh returned
/// ACCOUNT_DEACTIVATED / NO_MEMBERSHIP). The device must not keep the business's data: stop sync and wait for any
/// run in flight, tell the user (with the number of unsynced changes that can no longer be sent), wipe, then show
/// Login. Only a definite server answer sets the flag, so being offline never wipes anything. The flag survives a
/// kill, so a wipe that didn't finish runs again at the next start.
/// </summary>
public sealed class AccessRevokedService
{
    private readonly IAuthService _auth;
    private readonly SyncService _sync;
    private readonly INavigationService _navigation;
    private int _busy;

    public AccessRevokedService(IAuthService auth, SyncService sync, INavigationService navigation)
    {
        _auth = auth;
        _sync = sync;
        _navigation = navigation;
        _sync.AccessRevoked += (_, _) => MainThread.BeginInvokeOnMainThread(async () => await HandleAsync(null));
    }

    /// <summary>Returns true when access was revoked and handled (the caller should not continue into the app).</summary>
    public async Task<bool> HandleAsync(Page? host)
    {
        var code = await _auth.GetAccessRevokedAsync();
        if (code is null)
        {
            return false;
        }

        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return true;
        }

        try
        {
            await _sync.StopAsync();
            var businessId = await _auth.GetLocalDataBusinessIdAsync();
            var unsynced = string.IsNullOrWhiteSpace(businessId) ? 0 : await _sync.PendingCountForAsync(businessId);
            var page = host ?? Shell.Current;
            if (page is not null)
            {
                await page.DisplayAlert(Title(code), Message(code, unsynced), "OK");
            }

            if (!await _auth.WipeRevokedAccessAsync())
            {
                if (page is not null)
                {
                    await page.DisplayAlert(
                        "Couldn't clear this device",
                        "NeedleBase will try again next time it opens.",
                        "OK");
                }
            }

            await _navigation.ShowLoginAsync();
            return true;
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public static string Title(string code) =>
        code == AccessRevokedCodes.AccountDeactivated ? "Account turned off" : "Removed from the business";

    public static string Message(string code, int unsynced)
    {
        var why = code == AccessRevokedCodes.AccountDeactivated
            ? "This account has been turned off."
            : "This account is no longer part of the business.";
        var what = unsynced > 0
            ? $" {unsynced} change(s) on this device hadn't synced and can't be sent any more. The business's data will be removed from this device."
            : " The business's data will be removed from this device.";
        return why + what + " Ask your business owner if you need access again.";
    }
}
