using System.Net;
using System.Text.RegularExpressions;
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

        Assert.Contains("Stop chasing notebooks.", html);
        Assert.Contains("For tailors, seamstresses", html);
        Assert.Contains("Built for tailors, seamstresses and fashion workshop owners", html);
        Assert.Contains("Get set up for my workshop", html);
        Assert.Contains("The headaches of a busy fashion workshop", html);
        Assert.DoesNotContain("sewing shops", html);
        Assert.Contains("Who is NeedleBase for?", html);
        Assert.Contains("Tailoring shop software for Ghana", html);
        Assert.Contains("How it works", html);
        Assert.Contains("<html lang=\"en-GH\">", html);
        Assert.Contains("Tailoring shop software for Ghana &amp; West Africa", html);
        Assert.Contains("<title>", html);
        Assert.Contains("rel=\"canonical\" href=\"https://needlebase.example/\"", html);
        Assert.Contains("property=\"og:title\"", html);
        Assert.Contains("name=\"twitter:card\" content=\"summary_large_image\"", html);
        Assert.True(html.Contains("application/ld+json", StringComparison.Ordinal) || html.Contains("application/ld&#x2B;json", StringComparison.Ordinal), "JSON-LD script type missing");
        Assert.Contains("SoftwareApplication", html);
        Assert.Contains("WebSite", html);
        Assert.Contains("MobileApplication", html);
        Assert.Contains("FAQPage", html);
        Assert.Contains("ContactPoint", html);
        Assert.Contains("/images/og-needlebase.png", html);
        Assert.Contains("/images/needlebase-logo-nav", html);
        Assert.Contains("/images/needlebase-logo-clear", html);
        Assert.Contains("lp-brand", html);
        Assert.DoesNotContain("needlebase-mark-on-dark", html);
        Assert.Contains("Account/Register", html);
        Assert.Contains("github.com/Helmut-Essien/NeedleBase/releases/latest", html);
        Assert.DoesNotContain("nb-sidebar", html);

        // Sales CTAs: WhatsApp + email
        Assert.Contains("wa.me/233500785153", html);
        Assert.Contains("mailto:helmutessien@gmail.com", html);
        Assert.Contains("id=\"pricing\"", html);
        Assert.Contains("GH₵", html);
        Assert.Contains("Starter", html);
        Assert.Contains("Workshop", html);
        Assert.Contains("Production", html);
        Assert.DoesNotContain("\"@type\": \"Offer\"", html); // no placeholder prices in JSON-LD
        Assert.DoesNotContain("\"@@type\": \"Offer\"", html);

        // Single primary hero CTA
        Assert.Contains("Get set up for my workshop", html);
        Assert.Contains("Chat with us on WhatsApp", html);
        Assert.Contains("data-lp-hero-cta", html);
        var heroStart = html.IndexOf("id=\"top\"", StringComparison.Ordinal);
        var heroEnd = html.IndexOf("id=\"problems\"", heroStart >= 0 ? heroStart : 0, StringComparison.Ordinal);
        Assert.True(heroStart >= 0 && heroEnd > heroStart, "hero bounds");
        var hero = html[heroStart..heroEnd];
        Assert.Contains("Get set up for my workshop", hero);
        Assert.Contains("Run your fashion workshop from one place.", hero);
        Assert.Contains("Opens a WhatsApp chat with us", hero);
        Assert.DoesNotContain("Get NeedleBase on WhatsApp", hero);
        Assert.Contains("Already have a key?", hero);
        Assert.Single(Regex.Matches(hero, "lp-btn--primary"));

        // Footer: no logo image; contact + pricing
        var footerStart = html.LastIndexOf("<footer", StringComparison.Ordinal);
        var footerEnd = html.IndexOf("</footer>", footerStart >= 0 ? footerStart : 0, StringComparison.Ordinal);
        Assert.True(footerStart >= 0 && footerEnd > footerStart, "footer missing");
        var footer = html[footerStart..(footerEnd + "</footer>".Length)];
        Assert.Contains("NeedleBase by HelmutCode", footer);
        Assert.Contains("Pricing", footer);
        Assert.Contains("Contact", footer);
        Assert.Contains("WhatsApp", footer);
        Assert.Contains("mailto:helmutessien@gmail.com", footer);
        Assert.Contains("href=\"/Terms\"", footer);
        Assert.Contains("href=\"/Privacy\"", footer);
        Assert.Contains("href=\"/account/delete\"", footer);
        Assert.Contains("Delete account", footer);
        Assert.DoesNotContain("lp-brand", footer);
        Assert.DoesNotContain("<img", footer);

        Assert.DoesNotContain("SQLite", html);
        Assert.DoesNotContain("offline-first", html);
        Assert.DoesNotContain("free trial", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Most shops choose this", html);
        Assert.DoesNotContain("id=\"proof\"", html);
        Assert.Contains("subject=", html);
        Assert.Contains("Choose Starter", html);
        Assert.Contains("Choose Workshop", html);
        Assert.Contains("Choose Production", html);
        Assert.Contains("Download the app", html);
        Assert.Contains("Prices exclude VAT", html);
        Assert.Contains("To start a paid plan, message us on WhatsApp or email and we will send a licence key", html);
        Assert.Contains("GH₵ 0", html);
        Assert.Contains("GH₵ 50", html);
        Assert.Contains("GH₵ 150", html);
        Assert.Contains("GH₵ 300", html);
        Assert.Contains("GH₵ 140", html);
        Assert.Contains("GH₵ 430", html);
        Assert.Contains("GH₵ 850", html);
        Assert.Contains("GH₵ 500", html);
        Assert.Contains("GH₵ 1,500", html);
        Assert.Contains("GH₵ 3,000", html);
        Assert.Contains("You plus 2 staff", html);
        Assert.Contains("You plus 8 staff", html);
        Assert.Contains("You plus 20 staff", html);
        Assert.Contains("Save a copy of your records", html);
        Assert.Contains("Not included", html);
        Assert.Contains("Staff can&#x27;t join", html);
        Assert.Contains("Not on a computer or the website", html);
        Assert.Contains("doesn&#x27;t free a slot", html);
        Assert.Contains("Records stay on that phone only", html);
        Assert.Contains("No summary emails", html);
        Assert.Contains("If you move to a paid plan, your records come with you.", html);
        Assert.Contains("How do I get started?", html);
        Assert.Contains("use it free on one phone straight away", html);
        Assert.Contains("What can't I do on Free?", html);
        Assert.Contains("deleting one doesn't give the space back", html);
        Assert.Contains("NeedleBase doesn't keep a copy", html);
        Assert.Contains("You don't need one for Free.", html);
        Assert.Contains("Staff can only join on a paid plan.", html);
        Assert.DoesNotContain("after your shop is registered", html);
        Assert.Contains("Your records are kept safe online", html);
        Assert.Contains("Works on all your shop&#x27;s phones and computers", html);
        Assert.Contains("lp-bill-quarter", html);
        Assert.DoesNotContain("149", html);
        Assert.DoesNotContain("349", html);
        Assert.DoesNotContain("649", html);
        Assert.Contains("The headaches of a busy fashion workshop", html);
        Assert.Contains("Three steps to get NeedleBase", html);
        Assert.Contains("Ready to organise your fashion workshop?", html);
        Assert.DoesNotContain("Get NeedleBase on WhatsApp", html);
        Assert.DoesNotContain("Sample prices", html);

        // Benefits deck: four cards, HUD, mobile swipe hint (behaviour is CSS/JS).
        Assert.Contains("id=\"benefits\"", html);
        Assert.Contains("One place for the work that fills your day", html);
        Assert.Contains("data-lp-deck-stage", html);
        Assert.Contains("data-lp-deck-dot", html);
        Assert.Contains("data-lp-deck-pos", html);
        Assert.Contains("Swipe to see the next card.", html);
        Assert.Contains("Scroll to deal the next card.", html);
        Assert.Contains("data-lp-deck-hint", html);
        Assert.Contains("Previous card", html);
        Assert.Contains("Next card", html);
        Assert.Equal(4, Regex.Matches(html, "data-lp-deck-card").Count);
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
        Assert.Contains("<loc>https://needlebase.example/account/delete</loc>", xml);
        Assert.DoesNotContain("/Dashboard", xml);
        Assert.DoesNotContain("/Orders", xml);
    }
}
