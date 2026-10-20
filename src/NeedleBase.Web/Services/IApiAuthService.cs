using NeedleBase.Application.Contracts.Auth;

namespace NeedleBase.Web.Services;

public interface IApiAuthService
{
    /// <summary>
    /// Friendly message for the last failed login/register (from the API ProblemDetails <c>code</c>);
    /// null after success.
    /// </summary>
    string? LastErrorMessage { get; }

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
