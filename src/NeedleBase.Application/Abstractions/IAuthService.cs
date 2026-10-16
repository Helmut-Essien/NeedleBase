using NeedleBase.Application.Contracts.Auth;

namespace NeedleBase.Application.Abstractions;

public interface IAuthService
{
    Task<AuthResult> RegisterBusinessAsync(RegisterBusinessRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<AuthResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<AuthResult> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default);
    Task LogoutAsync(string? refreshToken, string? userId, string? ipAddress, CancellationToken cancellationToken = default);
}

public sealed record AuthResult(
    bool Succeeded,
    TokenResponse? Tokens,
    AuthUserDto? User,
    string? Error,
    string? ErrorCode = null)
{
    public static AuthResult Ok(TokenResponse tokens, AuthUserDto user) =>
        new(true, tokens, user, null);

    public static AuthResult Fail(string error, string? code = null) =>
        new(false, null, null, error, code);
}
