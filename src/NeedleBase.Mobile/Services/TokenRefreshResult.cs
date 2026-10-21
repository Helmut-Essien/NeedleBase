using NeedleBase.Application.Contracts.Auth;

namespace NeedleBase.Mobile.Services;

/// <summary>
/// Outcome of an access-token refresh. Transient failures must not clear the stored session.
/// Only 401/403 from the refresh endpoint are an invalid session.
/// </summary>
public readonly record struct TokenRefreshResult(
    bool Succeeded,
    TokenResponse? Token,
    string Message,
    bool IsTransientFailure,
    string? ErrorCode = null)
{
    /// <summary>The server said this account is deactivated or no longer a member: local data must be wiped.</summary>
    public bool AccessRevoked => AccessRevokedCodes.IsRevoked(ErrorCode);

    public static TokenRefreshResult Ok(TokenResponse token, string message = "Refreshed") =>
        new(true, token, message, false);

    public static TokenRefreshResult Transient(string message) =>
        new(false, null, message, true);

    public static TokenRefreshResult InvalidSession(string message, string? errorCode = null) =>
        new(false, null, message, false, errorCode);
}

public static class AccessRevokedCodes
{
    public const string AccountDeactivated = "ACCOUNT_DEACTIVATED";
    public const string NoMembership = "NO_MEMBERSHIP";

    public static bool IsRevoked(string? code) => code is AccountDeactivated or NoMembership;
}
