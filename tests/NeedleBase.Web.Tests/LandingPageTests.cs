using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace NeedleBase.Web.Tests;

/// <summary>Public marketing landing, robots and sitemap (SEO + auth gate).</summary>
public class LandingPageTests : IClassFixture<LandingPageTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Api:BaseUrl", "https://needlebase.example/");
            builder.UseSetting("FrontendBaseUrl", "https://needlebase.example/");
            builder.UseSetting("Api:InProcess", "false");
            builder.ConfigureLogging(l => l.ClearProviders());
        }
    }

    private readonly HttpClient _anon;

    public LandingPageTests(Factory factory)
    {
        _anon = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://needlebase.example/")
        });
    }

    [Fact]
    public async Task Anonymous_home_is_the_landing_with_seo_tags()
    {
        using var response = await _anon.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Sew more.", html);
        Assert.Contains("Chase less paperwork.", html);
        Assert.Contains("How it works", html);
        Assert.Contains("Example", html);
        Assert.Contains("not real data", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<html lang=\"en-GH\">", html);
        Assert.Contains("rel=\"canonical\" href=\"https://needlebase.example/\"", html);
        Assert.Contains("property=\"og:title\"", html);
        Assert.Contains("name=\"twitter:card\" content=\"summary_large_image\"", html);
        Assert.True(html.Contains("application/ld+json", StringComparison.Ordinal) || html.Contains("application/ld&#x2B;json", StringComparison.Ordinal), "JSON-LD script type missing");
        Assert.Contains("SoftwareApplication", html);
        Assert.Contains("WebSite", html);
        Assert.Contains("MobileApplication", html);
        Assert.Contains("FAQPage", html);
        Assert.Contains("/images/og-needlebase.png", html);
        Assert.Contains("/images/needlebase-logo-nav", html);
        Assert.Contains("/images/needlebase-logo-clear", html); // JSON-LD / original stacked asset
        Assert.Contains("lp-brand", html);
        Assert.DoesNotContain("needlebase-mark-on-dark", html);
        Assert.Contains("Account/Register", html);
        Assert.Contains("github.com/Helmut-Essien/NeedleBase/releases/latest", html);
        Assert.Contains("licence key from Platform", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nb-sidebar", html);
        // Honest about WhatsApp: not claimed as live product messaging.
        Assert.Contains("not offered as a live feature", html);
    }

    [Fact]
    public async Task Robots_and_sitemap_are_public()
    {
        using var robots = await _anon.GetAsync("/robots.txt");
        Assert.Equal(HttpStatusCode.OK, robots.StatusCode);
        var body = await robots.Content.ReadAsStringAsync();
        Assert.Contains("Disallow: /Dashboard", body);
        Assert.Contains("Disallow: /api", body);
        Assert.Contains("Sitemap: https://needlebase.example/sitemap.xml", body);

        using var map = await _anon.GetAsync("/sitemap.xml");
        Assert.Equal(HttpStatusCode.OK, map.StatusCode);
        var xml = await map.Content.ReadAsStringAsync();
        Assert.Contains("<loc>https://needlebase.example/</loc>", xml);
        Assert.Contains("<loc>https://needlebase.example/Account/Register</loc>", xml);
        Assert.DoesNotContain("/Dashboard", xml);
        Assert.DoesNotContain("/Orders", xml);
    }
}
