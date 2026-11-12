using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace NeedleBase.Web.Tests;

/// <summary>Public Privacy Policy and Terms pages, plus sign-in and register entry points.</summary>
public class LegalPagesTests : IClassFixture<LegalPagesTests.Factory>
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

    public LegalPagesTests(Factory factory)
    {
        _anon = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://needlebase.example/")
        });
    }

    [Fact]
    public async Task Privacy_page_matches_the_grounded_policy()
    {
        using var response = await _anon.GetAsync("/Privacy");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        AssertPublishedLegalCommon(html);
        Assert.Contains("<h1>NeedleBase Privacy Policy</h1>", html);
        Assert.Equal(1, CountIgnoreCase(html, "<h1"));
        Assert.Contains("do <strong>not</strong> include a way to take or upload a customer photo", html);
        Assert.Contains("NeedleBaseAuth", html);
        Assert.Contains("NeedleBase.AntiForgery", html);
        Assert.Contains("do not sell personal data", html);
        Assert.Contains("needlebase.runasp.net/Privacy", html);
        Assert.Contains("no in-app or self-service button", html);
        Assert.Contains("tailors, seamstresses, dressmakers and fashion workshop owners", html);
        Assert.DoesNotContain("sewing shops", html);
    }

    [Fact]
    public async Task Terms_page_matches_the_published_terms()
    {
        using var response = await _anon.GetAsync("/Terms");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        AssertPublishedLegalCommon(html);
        Assert.Contains("<h1>NeedleBase Terms and Conditions</h1>", html);
        Assert.Equal(1, CountIgnoreCase(html, "<h1"));
        Assert.Contains("no in-app billing", html);
        Assert.Contains("sample plan prices", html);
        Assert.Contains("licence key", html);
        Assert.Contains("as agreed in writing when the key is issued", html);
        Assert.Contains("licence fees you paid us in the 12 months", html);
        Assert.Contains("laws of the Republic of Ghana", html);
        Assert.Contains("no in-app or self-service button", html);
        Assert.DoesNotContain("without a subscription charge", html);
        Assert.DoesNotContain("19 October 2026", html);
        Assert.DoesNotContain("sewing shops", html);
    }

    [Fact]
    public async Task Login_and_register_link_to_privacy_and_terms()
    {
        using var login = await _anon.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginHtml = await login.Content.ReadAsStringAsync();
        Assert.Contains("href=\"/Privacy\"", loginHtml);
        Assert.Contains("href=\"/Terms\"", loginHtml);
        Assert.Contains("Privacy Policy", loginHtml);
        Assert.Contains("Terms and Conditions", loginHtml);
        Assert.Contains("nb-auth-legal", loginHtml);

        using var register = await _anon.GetAsync("/Account/Register");
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var registerHtml = await register.Content.ReadAsStringAsync();
        Assert.Contains("href=\"/Privacy\"", registerHtml);
        Assert.Contains("href=\"/Terms\"", registerHtml);
        Assert.Contains("Privacy Policy", registerHtml);
        Assert.Contains("Terms and Conditions", registerHtml);
        Assert.Contains("I agree to the", registerHtml);
    }

    private static void AssertPublishedLegalCommon(string html)
    {
        Assert.DoesNotContain("[TO CONFIRM", html);
        Assert.DoesNotContain("Draft for review", html);
        Assert.DoesNotContain("not legal advice", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("to be inserted", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HelmutCode Solutions", html);
        Assert.Contains("YN13 Flask Street, Ghana", html);
        Assert.Contains("Effective date:</strong> 1 January 2027", html);
        Assert.Contains("Last updated:</strong> 12 November 2026", html);
        Assert.Contains("helmutessien@gmail.com", html);
        Assert.Contains("mailto:helmutessien@gmail.com", html);
        Assert.Contains("wa.me/233500785153", html);
        Assert.Contains("+233 50 078 5153", html);
        Assert.Contains("Act 843", html);
        Assert.Contains("MonsterASP.NET", html);
        Assert.Contains("Europe", html);
        Assert.Contains("transferred outside Ghana", html);
        Assert.Contains("one year", html);
        Assert.Contains("730 days", html);
        Assert.Contains("30 days", html);
        Assert.Contains("HelmutCode Solutions is not registered with the Data Protection Commission", html);
        Assert.Contains("Gmail SMTP", html);
        Assert.Contains("Resend", html);
        Assert.Contains("Meta", html);
        Assert.Contains("not encrypted", html);
    }

    private static int CountIgnoreCase(string html, string token)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var i = html.IndexOf(token, start, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
            {
                return count;
            }

            count++;
            start = i + token.Length;
        }
    }
}
