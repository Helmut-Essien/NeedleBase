using NeedleBase.Application.Contracts.Auth;

namespace NeedleBase.Mobile.Services;

public interface IAuthService
{
    Task<AuthUserDto?> RegisterBusinessAsync(RegisterBusinessRequest request, CancellationToken cancellationToken = default);
    Task<AuthUserDto?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<TokenRefreshResult> RefreshAsync(CancellationToken cancellationToken = default);
    Task<bool> HasStoredTokensAsync();
    Task ClearTokensAsync();
    Task LogoutAsync(CancellationToken cancellationToken = default);
    Task<string?> GetAccessTokenAsync();
    Task StoreTokensAsync(TokenResponse tokens);
}
