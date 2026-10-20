using System.Reflection;

namespace NeedleBase.Mobile.Configuration;

/// <summary>
/// Resolves the API base URL. Debug keeps the local dev behaviour; Release reads the
/// build-time MSBuild property <c>NeedleBaseReleaseApiBaseUrl</c> (stamped as assembly metadata).
/// </summary>
public static class ApiEndpoint
{
    public const string MetadataKey = "NeedleBaseReleaseApiBaseUrl";

    /// <summary>Default when no URL is supplied at build time. Never resolves (RFC 6761 .invalid).</summary>
    public const string PlaceholderHost = "api.needlebase-placeholder.invalid";

    /// <summary>Release URL baked in at build time (always ends with '/'), or null if missing/invalid.</summary>
    public static string? ReleaseBaseUrl(Assembly? assembly = null)
    {
        var value = (assembly ?? typeof(ApiEndpoint).Assembly)
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == MetadataKey)?.Value;
        return Normalize(value);
    }

    /// <summary>Requires an absolute https URL; returns it with a trailing slash, else null.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var text = uri.ToString();
        return text.EndsWith('/') ? text : text + "/";
    }

    public static bool IsPlaceholder(string? baseUrl) =>
        baseUrl is not null &&
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Host, PlaceholderHost, StringComparison.OrdinalIgnoreCase);
}
