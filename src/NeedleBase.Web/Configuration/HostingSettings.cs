using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace NeedleBase.Web.Configuration;

/// <summary>
/// Forwarded-header trust and the key-ring folder, from settings (docs/production-config.md). Same rules as the Api's
/// ProductionConfigValidator: strict IPs (no "1" = 0.0.0.1, no octal/port forms), CIDR with prefix >= 1 (never /0,
/// which would let anyone spoof X-Forwarded-For/-Proto). Bad values stop start-up with the setting name.
/// </summary>
public static class HostingSettings
{
    public static ForwardedHeadersOptions ForwardedHeaders(IConfiguration configuration)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                               Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };

        foreach (var proxy in Split(configuration["ForwardedHeaders:KnownProxies"]))
        {
            if (!TryParseIp(proxy, out var address))
            {
                throw new InvalidOperationException($"ForwardedHeaders:KnownProxies entry '{proxy}' is not an IP address.");
            }

            options.KnownProxies.Add(address);
        }

        foreach (var network in Split(configuration["ForwardedHeaders:KnownNetworks"]))
        {
            var slash = network.IndexOf('/');
            if (slash <= 0 || !TryParseIp(network[..slash], out var prefixAddress) ||
                !int.TryParse(network[(slash + 1)..], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var prefix) ||
                prefix < 1 || prefix > (prefixAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownNetworks entry '{network}' is not a CIDR range (e.g. 10.0.0.0/8), or is /0 (trust everyone).");
            }

            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefixAddress, prefix));
        }

        return options;
    }

    /// <summary>Absolute key folder from DataProtection:KeysPath (trimmed), or null when unset.</summary>
    public static string? KeysPath(IConfiguration configuration)
    {
        var value = configuration["DataProtection:KeysPath"]?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return Path.IsPathRooted(value)
            ? value
            : throw new InvalidOperationException("DataProtection:KeysPath must be an absolute folder path.");
    }

    public static DirectoryInfo EnsureKeysFolder(string path)
    {
        try
        {
            return Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(
                $"Data Protection key folder can't be created or written ({ex.GetType().Name}). Give the app pool write access " +
                "or set DataProtection__KeysPath to a writable absolute folder (docs/production-config.md).", ex);
        }
    }

    internal static bool TryParseIp(string value, out IPAddress address)
    {
        address = IPAddress.None;
        if (!IPAddress.TryParse(value, out var parsed) ||
            (parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                ? parsed.ToString() != value
                : value.Contains('[') || value.Contains(']')))
        {
            return false;
        }

        address = parsed;
        return true;
    }

    private static IEnumerable<string> Split(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
