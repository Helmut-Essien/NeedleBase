using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using NeedleBase.Api.Configuration;

namespace NeedleBase.Api.Tests;

public class ForwardedHeadersTrustTests
{
    private static ForwardedHeadersOptions Options(string? proxies = null, string? networks = null)
    {
        var options = new ForwardedHeadersOptions();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies"] = proxies,
            ["ForwardedHeaders:KnownNetworks"] = networks
        }).Build();
        ForwardedHeadersConfig.Apply(options, config);
        return options;
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("203.0.113.7", false)]
    public void Loopback_only_by_default(string peer, bool trusted) =>
        Assert.Equal(trusted, ForwardedHeadersConfig.IsTrusted(Options(), IPAddress.Parse(peer)));

    [Fact]
    public void Known_proxy_and_network_are_trusted()
    {
        var options = Options("203.0.113.7", "198.51.100.0/24");
        Assert.True(ForwardedHeadersConfig.IsTrusted(options, IPAddress.Parse("203.0.113.7")));
        Assert.True(ForwardedHeadersConfig.IsTrusted(options, IPAddress.Parse("::ffff:203.0.113.7")));
        Assert.True(ForwardedHeadersConfig.IsTrusted(options, IPAddress.Parse("198.51.100.20")));
        Assert.False(ForwardedHeadersConfig.IsTrusted(options, IPAddress.Parse("203.0.113.8")));
    }

    [Fact]
    public void No_peer_is_not_trusted() => Assert.False(ForwardedHeadersConfig.IsTrusted(Options(), null));
}
