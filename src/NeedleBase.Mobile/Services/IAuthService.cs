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

    /// <summary>Checks an invite code (shop, role, email). Null + <see cref="LastErrorMessage"/> on failure.</summary>
    Task<NeedleBase.Application.Contracts.Staff.InvitePreviewDto?> PreviewInviteAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult<NeedleBase.Application.Contracts.Staff.InvitePreviewDto?>(null);

    /// <summary>Creates the account from an invite and signs in (same device-data rules as sign-in).</summary>
    Task<AuthUserDto?> AcceptInviteAsync(AcceptInviteRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<AuthUserDto?>(null);

    /// <summary>Changes the password; other devices are signed out, this one stores the new tokens.</summary>
    Task<bool> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    /// <summary>Whether the server can email password-reset codes. Null when the server can't be reached.</summary>
    Task<bool?> EmailEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult<bool?>(false);

    /// <summary>Ask for a reset code by email. True when accepted (same answer whether or not the account exists).</summary>
    Task<bool> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>Set a new password with the emailed code. Signs out every device.</summary>
    Task<bool> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default) => Task.FromResult(false);

    Task<string?> GetBusinessIdAsync();
    Task<AuthUserDto?> GetCachedUserAsync();
}
