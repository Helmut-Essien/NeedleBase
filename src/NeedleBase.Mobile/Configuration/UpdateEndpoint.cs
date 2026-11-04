using System.Reflection;

namespace NeedleBase.Mobile.Configuration;

/// <summary>
/// Over-the-air update settings stamped at build time (docs/android-release.md, "Updates"):
/// <c>NeedleBaseUpdateChannel</c> = <c>sideload</c> (APK from GitHub Releases, in-app updates on) or <c>play</c>
/// (Google Play updates the app; the in-app updater is compiled out and REQUEST_INSTALL_PACKAGES is not declared),
/// and <c>NeedleBaseUpdateManifestUrl</c> = the https address that serves <c>update.json</c>
/// (the app's own Api, <c>https://SITE/api/mobile/update</c>, AssetTag style).
/// </summary>
public static class UpdateEndpoint
{
    public const string ChannelKey = "NeedleBaseUpdateChannel";
    public const string ManifestKey = "NeedleBaseUpdateManifestUrl";
    public const string Sideload = "sideload";

    /// <summary>The manifest URL when this build updates itself, else null (Play build, no URL, or not https).</summary>
    public static string? ManifestUrl(Assembly? assembly = null)
    {
        var attributes = (assembly ?? typeof(UpdateEndpoint).Assembly).GetCustomAttributes<AssemblyMetadataAttribute>().ToList();
        var channel = attributes.FirstOrDefault(a => a.Key == ChannelKey)?.Value;
        var url = attributes.FirstOrDefault(a => a.Key == ManifestKey)?.Value;
        return Resolve(channel, url);
    }

    public static string? Resolve(string? channel, string? url)
    {
        if (!string.Equals(channel?.Trim(), Sideload, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.ToString();
    }
}
