namespace NeedleBase.Infrastructure.Licensing;

// Mirrors Platform's Shared/Dtos/Licenses/ValidateLicense*.cs (camelCase JSON) plus the email-match fields NeedleBase
// needs Platform to add (see docs/platform-licensing.md).

internal sealed class PlatformValidateRequest
{
    public required string LicenseKey { get; init; }
    public string? ServiceCode { get; init; }
    public string? DeviceId { get; init; }
    public string? DeviceName { get; init; }

    /// <summary>Owner email to compare with the license's customer (registration/attach only). Platform must not echo it.</summary>
    public string? Email { get; init; }
}

internal sealed class PlatformValidateResponse
{
    public bool IsValid { get; set; }
    public string? PlanName { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Message { get; set; }
    public string? Reason { get; set; }
    public DateTime ServerTime { get; set; }
    public string? LicenseId { get; set; }
    public string? Token { get; set; }
    public DateTime? RefreshAfter { get; set; }
    public DateTime? OfflineGraceUntil { get; set; }
    public int? MaxDevices { get; set; }
    public int? ActiveDevices { get; set; }

    /// <summary>True/false only when an email was sent; null = Platform didn't answer it (treated as a mismatch).</summary>
    public bool? EmailMatches { get; set; }
}

internal sealed class PlatformErrorResponse
{
    public string? Reason { get; set; }
    public string? Message { get; set; }
    public int RetryAfterSeconds { get; set; }
    public bool Retryable { get; set; } = true;
}

internal sealed class PlatformJwks
{
    public List<PlatformJwk> Keys { get; set; } = [];
}

internal sealed class PlatformJwk
{
    public string? Kty { get; set; }
    public string? Crv { get; set; }
    public string? Kid { get; set; }
    public string? X { get; set; }
    public string? Y { get; set; }
}
