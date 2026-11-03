using NeedleBase.Domain.Entities;

namespace NeedleBase.Application.Licensing;

/// <summary>Platform's <c>POST /api/licenses/validate</c> (plus JWKS token verification). Server-only.</summary>
public interface ILicensePlatform
{
    /// <summary>False when <c>Platform:BaseUrl</c> / <c>Platform:IntegrationKey</c> are not set.</summary>
    bool IsConfigured { get; }

    Task<LicensePlatformOutcome> ValidateAsync(LicenseValidateCall call, CancellationToken cancellationToken = default);
}

/// <param name="DeviceId">The shop's id (one shop per key: Platform MaxDevices = 1).</param>
/// <param name="OwnerEmail">Only at registration / attach: Platform says whether it matches the license's customer.</param>
public sealed record LicenseValidateCall(string LicenseKey, string DeviceId, string? DeviceName, string? OwnerEmail);

/// <summary>
/// Either a definite answer (<see cref="Check"/>) or "couldn't tell" (<see cref="Unavailable"/>: network, timeout,
/// 429, 5xx, bad/unsigned token, NeedleBase's own config problem). "Couldn't tell" never locks a shop.
/// </summary>
public sealed record LicensePlatformOutcome(
    LicenseCheck? Check,
    bool? EmailMatches,
    bool Unavailable,
    TimeSpan? RetryAfter = null,
    string? UnavailableReason = null)
{
    public static LicensePlatformOutcome Judged(LicenseCheck check, bool? emailMatches = null) => new(check, emailMatches, false);

    public static LicensePlatformOutcome NotAvailable(string reason, TimeSpan? retryAfter = null) =>
        new(null, null, true, retryAfter, reason);
}
