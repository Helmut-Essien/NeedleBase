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
    Task<string?> GetBusinessIdAsync();
    Task<AuthUserDto?> GetCachedUserAsync();
}
