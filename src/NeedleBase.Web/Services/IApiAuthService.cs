using NeedleBase.Application.Contracts.Auth;

namespace NeedleBase.Web.Services;

public interface IApiAuthService
{
    Task<AuthSessionResult?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<AuthSessionResult?> RegisterBusinessAsync(
        RegisterBusinessRequest request,
        CancellationToken cancellationToken = default);

    Task<TokenResponse?> RefreshAsync(
        string accessToken,
        string refreshToken,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default);
}

public sealed record AuthSessionResult(TokenResponse Tokens, AuthUserDto User);
