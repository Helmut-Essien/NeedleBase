using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NeedleBase.Web.Tests;

/// <summary>Start-up settings checks: forwarded-header proxies and the key ring path fail fast with the setting name.</summary>
public class StartupConfigTests
{
    private sealed class Factory(Dictionary<string, string> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Api:BaseUrl", "http://127.0.0.1:1/");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureLogging(l => l.ClearProviders());
        }
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies", "not-an-ip", "KnownProxies")]
    [InlineData("ForwardedHeaders:KnownNetworks", "10.0.0.0/99", "KnownNetworks")]
    [InlineData("DataProtection:KeysPath", "relative/keys", "DataProtection:KeysPath")]
    [InlineData("ForwardedHeaders:KnownProxies", "10.1", "KnownProxies")]
    [InlineData("ForwardedHeaders:KnownProxies", "010.0.0.1", "KnownProxies")]
    [InlineData("ForwardedHeaders:KnownNetworks", "0.0.0.0/0", "trust everyone")]
    public void Bad_setting_stops_startup_with_its_name(string key, string value, string expected)
    {
        using var factory = new Factory(new() { [key] = value });
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var inner = ex as InvalidOperationException ?? ex.InnerException as InvalidOperationException ?? ex.GetBaseException();
        Assert.Contains(expected, inner.Message);
    }

    [Fact]
    public async Task Good_proxy_settings_start()
    {
        using var factory = new Factory(new()
        {
            ["ForwardedHeaders:KnownProxies"] = "10.0.0.1",
            ["ForwardedHeaders:KnownNetworks"] = "10.0.0.0/8"
        });
        var response = await factory.CreateClient().GetAsync("/Privacy");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public void Hosting_settings_parse_good_values()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ForwardedHeaders:KnownProxies"] = "10.0.0.1, ::1",
                ["ForwardedHeaders:KnownNetworks"] = "10.0.0.0/8,fd00::/8",
                ["DataProtection:KeysPath"] = "  "
            }).Build();
        var options = NeedleBase.Web.Configuration.HostingSettings.ForwardedHeaders(config);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Contains(options.KnownProxies, p => p.ToString() == "10.0.0.1");
        Assert.Equal(3, options.KnownNetworks.Count); // + framework loopback
        Assert.Null(NeedleBase.Web.Configuration.HostingSettings.KeysPath(config));
    }
}
