using System.Net;

namespace NeedleBase.Api.Configuration;

/// <summary>
/// Fail-fast check of the settings a real deployment needs (docs/production-config.md). Runs outside Development and
/// outside the "Testing" environment, before the host starts, so a half-configured MonsterASP site stops at start-up
/// with one clear list instead of failing on the first request. Errors stop start-up; warnings are only logged
/// (optional features that are simply off). Never echoes secret values.
/// </summary>
public static class ProductionConfigValidator
{
    /// <summary>Marker used in the committed Development JWT key; it must never reach a real deployment.</summary>
    private const string DevelopmentKeyMarker = "Not_For_Production";

    public static bool ShouldValidate(IHostEnvironment environment) =>
        !environment.IsDevelopment() && !environment.IsEnvironment("Testing");

    public static ValidationResult Validate(IConfiguration configuration)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
        {
            errors.Add("ConnectionStrings__DefaultConnection is missing (the PostgreSQL connection string).");
        }
        else
        {
            try
            {
                _ = NeedleBase.Infrastructure.Persistence.DatabaseConnection.Build(configuration);
            }
            catch (InvalidOperationException)
            {
                errors.Add("ConnectionStrings__DefaultConnection is not a valid PostgreSQL (Npgsql) connection string.");
            }
        }

        var jwtKey = configuration["JwtSettings:SecurityKey"];
        if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
        {
            errors.Add("JwtSettings__SecurityKey is missing or shorter than 32 characters (use a long random value).");
        }
        else if (jwtKey.Contains(DevelopmentKeyMarker, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("JwtSettings__SecurityKey is the Development key. Set a new long random value for this server.");
        }

        RequireHttpsUrl(configuration["FrontendBaseUrl"], "FrontendBaseUrl",
            "the public web address, e.g. https://needlebase.helmutcode.com", errors);

        // Platform licensing: required unless the operator explicitly turned the key requirement off.
        var requireKey = true;
        if (configuration["Platform:RequireLicenseKey"] is { } requireRaw && !string.IsNullOrWhiteSpace(requireRaw))
        {
            if (!bool.TryParse(requireRaw.Trim(), out requireKey))
            {
                errors.Add("Platform__RequireLicenseKey must be true or false.");
                requireKey = true;
            }
        }

        var platformUrl = configuration["Platform:BaseUrl"];
        var platformKey = configuration["Platform:IntegrationKey"];
        if (requireKey)
        {
            RequireHttpsUrl(platformUrl, "Platform__BaseUrl", "the Platform licensing address", errors);
            if (string.IsNullOrWhiteSpace(platformKey))
            {
                errors.Add("Platform__IntegrationKey is missing (from Platform, for the NEEDLEBASE service).");
            }
        }
        else
        {
            warnings.Add("Platform:RequireLicenseKey is false: new shops register without a license key and are never locked.");
        }

        if (string.IsNullOrWhiteSpace(configuration["Platform:TokenIssuer"]) && requireKey)
        {
            warnings.Add("Platform:TokenIssuer is empty: the license token issuer is not checked.");
        }

        // Email: off when nothing is set; a half-filled section is a mistake.
        var email = configuration.GetSection("Email");
        var emailKeys = new[] { "Host", "Username", "Password" };
        var emailSet = emailKeys.Count(k => !string.IsNullOrWhiteSpace(email[k]));
        if (emailSet == 0)
        {
            warnings.Add("Email is not configured: invite/reset codes must be shared by hand and Forgot password is hidden.");
        }
        else if (emailSet < emailKeys.Length)
        {
            errors.Add("Email is half configured: set all of Email__Host, Email__Username and Email__Password (or none).");
        }

        if (!string.IsNullOrWhiteSpace(email["Port"]) && (!int.TryParse(email["Port"]!.Trim(), out var p) || p is <= 0 or >= 65536))
        {
            errors.Add("Email__Port must be a number between 1 and 65535.");
        }

        // WhatsApp: off when nothing is set; token without phone id (or the reverse) is a mistake.
        var wa = configuration.GetSection("WhatsApp");
        var hasToken = !string.IsNullOrWhiteSpace(wa["AccessToken"]);
        var hasPhone = !string.IsNullOrWhiteSpace(wa["PhoneNumberId"]);
        if (hasToken != hasPhone)
        {
            errors.Add("WhatsApp is half configured: set both WhatsApp__AccessToken and WhatsApp__PhoneNumberId (or neither).");
        }
        else if (!hasToken)
        {
            warnings.Add("WhatsApp is not configured: automatic status messages are off (tap-to-send still works).");
        }
        else
        {
            if (!wa.GetSection("Templates").GetChildren().Any(t => !string.IsNullOrWhiteSpace(t.Value)))
            {
                errors.Add("WhatsApp__Templates__<Status> must name at least one approved template when WhatsApp is on.");
            }

            if (string.IsNullOrWhiteSpace(wa["AppSecret"]) || string.IsNullOrWhiteSpace(wa["VerifyToken"]))
            {
                warnings.Add("WhatsApp webhook is off (WhatsApp__AppSecret / WhatsApp__VerifyToken unset): delivery/opt-out replies are not received.");
            }
        }

        foreach (var proxy in Split(configuration["ForwardedHeaders:KnownProxies"]))
        {
            if (!TryParseIp(proxy, out _))
            {
                errors.Add($"ForwardedHeaders__KnownProxies has an entry that is not an IP address: '{proxy}'.");
            }
        }

        foreach (var network in Split(configuration["ForwardedHeaders:KnownNetworks"]))
        {
            if (!TryParseNetwork(network))
            {
                errors.Add($"ForwardedHeaders__KnownNetworks has an entry that is not a CIDR range (e.g. 10.0.0.0/8), or is /0 (trust everyone): '{network}'.");
            }
        }

        if (configuration["DataProtection:KeysPath"] is { } keysPath && !string.IsNullOrWhiteSpace(keysPath) &&
            !Path.IsPathRooted(keysPath.Trim()))
        {
            errors.Add("DataProtection__KeysPath must be an absolute folder path.");
        }

        return new ValidationResult(errors, warnings);
    }

    /// <summary>Validates; throws one exception listing every error. Warnings go to <paramref name="logWarning"/>.</summary>
    public static void ThrowIfInvalid(IConfiguration configuration, Action<string> logWarning)
    {
        var result = Validate(configuration);
        foreach (var warning in result.Warnings)
        {
            logWarning(warning);
        }

        if (result.Errors.Count > 0)
        {
            throw new InvalidOperationException(
                "NeedleBase Api is not configured for production. Fix these settings (see docs/production-config.md):"
                + Environment.NewLine + string.Join(Environment.NewLine, result.Errors.Select(e => " - " + e)));
        }
    }

    internal static IEnumerable<string> Split(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Strict IP parse: IPAddress.TryParse alone accepts shorthand such as "1" (0.0.0.1) or "10.1" (10.0.0.1), which
    /// would silently trust the wrong host.
    /// </summary>
    internal static bool TryParseIp(string value, out IPAddress address)
    {
        address = IPAddress.None;
        if (!IPAddress.TryParse(value, out var parsed))
        {
            return false;
        }

        // IPv4 must round-trip exactly: rejects "1", "10.1", leading zeros (read as octal: 010.0.0.1 = 8.0.0.1) and
        // "a.b.c.d:port". IPv6 may be written in several valid forms, so only bracket/port forms are refused.
        if (parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                ? parsed.ToString() != value
                : value.Contains('[') || value.Contains(']'))
        {
            return false;
        }

        address = parsed;
        return true;
    }

    /// <summary>CIDR "address/prefix". A /0 prefix (trust every address) is refused: anyone could spoof their IP.</summary>
    internal static bool TryParseNetwork(string value)
    {
        var slash = value.IndexOf('/');
        if (slash <= 0 || !TryParseIp(value[..slash], out var address) ||
            !int.TryParse(value[(slash + 1)..], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var prefix))
        {
            return false;
        }

        var max = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        return prefix >= 1 && prefix <= max;
    }

    private static void RequireHttpsUrl(string? value, string name, string what, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{name} is missing ({what}).");
        }
        else if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{name} must be an absolute https URL ({what}).");
        }
    }

    public sealed record ValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
    {
        public bool IsValid => Errors.Count == 0;
    }
}
