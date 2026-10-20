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
