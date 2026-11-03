namespace NeedleBase.Infrastructure.Licensing;

/// <summary>
/// "Platform" section: the license hub that issues NeedleBase keys. <see cref="IntegrationKey"/> comes only from
/// user-secrets or environment variables (Platform__IntegrationKey), never appsettings, source control or the phone.
/// </summary>
public sealed class PlatformOptions
{
    public const string Section = "Platform";

    /// <summary>e.g. https://platform.example.com (no trailing path).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Platform's integration key (sent as X-Integration-Key). Secret.</summary>
    public string? IntegrationKey { get; set; }

    /// <summary>NeedleBase's service product code in Platform; also the token audience.</summary>
    public string ServiceCode { get; set; } = "NEEDLEBASE";

    /// <summary>Expected token issuer (Platform's Licensing:TokenIssuer). Empty = not checked.</summary>
    public string? TokenIssuer { get; set; }

    /// <summary>
    /// New shops must enter a license key. Only turn off for local development/tests: with it off, registration
    /// without a key creates an unlicensed (never-locked) shop.
    /// </summary>
    public bool RequireLicenseKey { get; set; } = true;

    /// <summary>Days of full use after Platform first says "not valid" (on top of the token's own grace).</summary>
    public int LockGraceDays { get; set; } = 7;

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>How often the background re-check looks for shops that are due.</summary>
    public int RecheckPollMinutes { get; set; } = 15;

    public bool IsConfigured =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback) &&
        !string.IsNullOrWhiteSpace(IntegrationKey);
}
