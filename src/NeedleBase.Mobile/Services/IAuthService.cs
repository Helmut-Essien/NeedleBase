using NeedleBase.Application.Contracts.Auth;

namespace NeedleBase.Mobile.Services;

public interface IAuthService
{
    /// <summary>
    /// Friendly message for the last failed <see cref="LoginAsync"/> / <see cref="RegisterBusinessAsync"/>
    /// (from the API ProblemDetails <c>code</c>); null after success.
    /// </summary>
    string? LastErrorMessage => null;

    Task<AuthUserDto?> RegisterBusinessAsync(RegisterBusinessRequest request, CancellationToken cancellationToken = default);
    Task<AuthUserDto?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<TokenRefreshResult> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh only if the stored access token is still <paramref name="staleAccessToken"/> (checked under the
    /// app-wide refresh lock), so concurrent 401s cause one refresh, not several.
    /// </summary>
    Task<TokenRefreshResult> RefreshIfStaleAsync(string? staleAccessToken, CancellationToken cancellationToken = default) =>
        RefreshAsync(cancellationToken);
    Task<bool> HasStoredTokensAsync();
    Task ClearTokensAsync();
    /// <summary>Returns false if local data wipe failed (session kept).</summary>
    Task<bool> LogoutAsync(CancellationToken cancellationToken = default);
    Task<string?> GetAccessTokenAsync();
    Task StoreTokensAsync(TokenResponse tokens);
    Task StoreSessionAsync(AuthUserDto user);

    /// <summary>
    /// Asked before signing in as a different account/business would wipe unsynced changes (argument = how many).
    /// Return true to continue (wipe), false to cancel the sign-in. When unset, such a sign-in is refused.
    /// </summary>
    Func<int, Task<bool>>? OtherAccountDataPrompt
    {
        get => null;
        set { }
    }
    /// <summary>ACCOUNT_DEACTIVATED / NO_MEMBERSHIP when the server revoked access and the wipe is still due.</summary>
    Task<string?> GetAccessRevokedAsync() => Task.FromResult<string?>(null);

    /// <summary>Business whose data is on this device (kept after tokens are cleared).</summary>
    Task<string?> GetLocalDataBusinessIdAsync() => GetBusinessIdAsync();

    /// <summary>Access revoked: wipe local data and the session. False if the wipe failed (flag kept for retry).</summary>
    Task<bool> WipeRevokedAccessAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    Task<string?> GetBusinessIdAsync();
    Task<AuthUserDto?> GetCachedUserAsync();
}
