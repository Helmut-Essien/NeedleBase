using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace NeedleBase.Api.Configuration;

/// <summary>
/// Trusted proxies for X-Forwarded-For/-Proto: loopback (framework default) plus ForwardedHeaders:KnownProxies (IPs)
/// and ForwardedHeaders:KnownNetworks (CIDR), comma-separated. Shared by the Api and its tests; the Web host has the
/// same keys. Invalid entries throw with the setting name (production start-up validation reports them first).
/// </summary>
public static class ForwardedHeadersConfig
{
    public static void Apply(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        foreach (var proxy in ProductionConfigValidator.Split(configuration["ForwardedHeaders:KnownProxies"]))
        {
            if (!ProductionConfigValidator.TryParseIp(proxy, out var address))
            {
                throw new InvalidOperationException($"ForwardedHeaders:KnownProxies entry '{proxy}' is not an IP address.");
            }

            options.KnownProxies.Add(address);
        }

        foreach (var network in ProductionConfigValidator.Split(configuration["ForwardedHeaders:KnownNetworks"]))
        {
            if (!ProductionConfigValidator.TryParseNetwork(network))
            {
                throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks entry '{network}' is not a CIDR range (or is /0, which would trust everyone).");
            }

            options.KnownNetworks.Add(Microsoft.AspNetCore.HttpOverrides.IPNetwork.Parse(network));
        }
    }

    /// <summary>True when X-Forwarded-* from <paramref name="peer"/> is honoured (loopback, KnownProxies, KnownNetworks).</summary>
    public static bool IsTrusted(ForwardedHeadersOptions options, IPAddress? peer)
    {
        if (peer is null)
        {
            return false;
        }

        if (peer.IsIPv4MappedToIPv6)
        {
            peer = peer.MapToIPv4();
        }

        return IPAddress.IsLoopback(peer)
               || options.KnownProxies.Any(p => p.Equals(peer))
               || options.KnownNetworks.Any(n => n.Contains(peer));
    }
}
