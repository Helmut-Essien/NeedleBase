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

    /// <summary>Who an invite code is for; null with <see cref="LastErrorMessage"/> set when it can't be used.</summary>
    Task<NeedleBase.Application.Contracts.Staff.InvitePreviewDto?> PreviewInviteAsync(string code, CancellationToken cancellationToken = default);

    Task<AuthSessionResult?> AcceptInviteAsync(AcceptInviteRequest request, CancellationToken cancellationToken = default);

    /// <summary>True when the Api can send email (Forgot password is offered). Cached briefly; false when unreachable.</summary>
    Task<bool> EmailEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>Asks for a reset code. True when accepted (also for unknown emails); false with <see cref="LastErrorMessage"/>.</summary>
    Task<bool> RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Sets a new password with the emailed code. False with <see cref="LastErrorMessage"/>.</summary>
    Task<bool> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}

public sealed record AuthSessionResult(TokenResponse Tokens, AuthUserDto User);

/// <summary>Token response body (accessToken, refreshToken, user).</summary>
public sealed record AuthSessionPayload(string AccessToken, string RefreshToken, AuthUserDto User)
{
    public AuthSessionResult ToSession() => new(new TokenResponse(AccessToken, RefreshToken), User);
}
