using NeedleBase.Application.Contracts.Auth;

namespace NeedleBase.Application.Abstractions;

public interface IAuthService
{
    Task<AuthResult> RegisterBusinessAsync(RegisterBusinessRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<AuthResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<AuthResult> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default);
    Task LogoutAsync(string? refreshToken, string? userId, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Who the invite is for (anonymous; throttled per IP).</summary>
    Task<(NeedleBase.Application.Contracts.Staff.InvitePreviewDto? Preview, string? ErrorCode, TimeSpan? RetryAfter)> PreviewInviteAsync(
        string code, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Creates the invitee's account + membership and signs them in.</summary>
    Task<AuthResult> AcceptInviteAsync(AcceptInviteRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the current password, sets the new one, signs out every other session (security stamp + refresh tokens)
    /// and returns fresh tokens for this one. Audited as auth.password_changed (no secrets).
    /// </summary>
    Task<AuthResult> ChangePasswordAsync(string userId, ChangePasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Emails a one-time reset code when the address belongs to an active account. The reply is the same either way
    /// (no account-existence leak). Fails with PASSWORD_RESET_UNAVAILABLE when email isn't configured.
    /// </summary>
    Task<AuthResult> RequestPasswordResetAsync(ForgotPasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Sets a new password with an emailed code and signs out every device. Succeeds with no tokens (sign in next).</summary>
    Task<AuthResult> ResetPasswordAsync(ResetPasswordRequest request, string? ipAddress, CancellationToken cancellationToken = default);
}

public sealed record AuthResult(
    bool Succeeded,
    TokenResponse? Tokens,
    AuthUserDto? User,
    string? Error,
    string? ErrorCode = null,
    TimeSpan? RetryAfter = null)
{
    public const string LoginThrottledCode = "LOGIN_THROTTLED";

    public static AuthResult Throttled(TimeSpan retryAfter) =>
        new(false, null, null, "Too many wrong tries from here. Wait a moment, then try again.", LoginThrottledCode, retryAfter);

    public static AuthResult Ok(TokenResponse tokens, AuthUserDto user) =>
        new(true, tokens, user, null);

    /// <summary>Success with nothing to return (password reset requested / done).</summary>
    public static AuthResult Done() => new(true, null, null, null);

    public static AuthResult Fail(string error, string? code = null) =>
        new(false, null, null, error, code);
}
