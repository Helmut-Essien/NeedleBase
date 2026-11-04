using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace NeedleBase.Api.Tests;

/// <summary>The Api refuses to start in Production without its critical settings, naming them all at once.</summary>
public class ProductionStartupTests
{
    private sealed class ProductionFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            // Blank out anything the machine environment might provide.
            builder.UseSetting("ConnectionStrings:DefaultConnection", "");
            builder.UseSetting("JwtSettings:SecurityKey", "");
            builder.UseSetting("FrontendBaseUrl", "");
            builder.UseSetting("Platform:BaseUrl", "");
            builder.UseSetting("Platform:IntegrationKey", "");
            builder.ConfigureLogging(l => l.ClearProviders());
        }
    }

    [Fact]
    public void Production_without_settings_fails_fast_listing_them()
    {
        using var factory = new ProductionFactory();
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var message = (ex as InvalidOperationException ?? ex.GetBaseException()).Message;
        Assert.Contains("not configured for production", message);
        Assert.Contains("ConnectionStrings__DefaultConnection", message);
        Assert.Contains("JwtSettings__SecurityKey", message);
        Assert.Contains("Platform__IntegrationKey", message);
    }
}
