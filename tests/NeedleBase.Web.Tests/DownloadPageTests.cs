using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NeedleBase.Web.Downloads;

namespace NeedleBase.Web.Tests;

/// <summary>Public /download page: stable and beta APKs, install steps, WhatsApp help.</summary>
public class DownloadPageTests
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

    private static readonly string Releases = "https://github.com/Helmut-Essien/NeedleBase/releases";
    private static readonly string LatestPage = $"{Releases}/latest";

    private static readonly AndroidBuild StableBuild = new(
        "1.0.0",
        "mobile-v1.0.0",
        "needlebase.apk",
        "https://github.com/Helmut-Essien/NeedleBase/releases/download/mobile-v1.0.0/needlebase.apk",
        65_000_000,
        "https://github.com/Helmut-Essien/NeedleBase/releases/tag/mobile-v1.0.0");

    private static readonly AndroidBuild BetaBuild = new(
        "1.0.1",
        "mobile-v1.0.1",
        "needlebase-1.0.1-20.apk",
        "https://github.com/Helmut-Essien/NeedleBase/releases/download/mobile-v1.0.1/needlebase-1.0.1-20.apk",
        65_100_000,
        "https://github.com/Helmut-Essien/NeedleBase/releases/tag/mobile-v1.0.1");

    private static readonly AndroidBuild CurrentBeta = new(
        "0.1.2",
        "mobile-v0.1.2",
        "needlebase-0.1.2-18.apk",
        "https://github.com/Helmut-Essien/NeedleBase/releases/download/mobile-v0.1.2/needlebase-0.1.2-18.apk",
        64_742_777,
        "https://github.com/Helmut-Essien/NeedleBase/releases/tag/mobile-v0.1.2");

    private sealed class StubCatalog(AndroidDownloadLinks links) : IAndroidReleaseCatalog
    {
        public Task<AndroidDownloadLinks> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(links);
    }

    private static WebApplicationFactory<Program> Host(AndroidDownloadLinks links) =>
        new Factory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAndroidReleaseCatalog>();
                services.AddSingleton<IAndroidReleaseCatalog>(new StubCatalog(links));
            });
        });

    private static HttpClient Client(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://needlebase.example/")
        });

    private static void AssertShared(string html)
    {
        Assert.Contains("Download the Android app", html);
        Assert.Contains("Download the app file to your phone.", html);
        Assert.Contains("Allow install from this source when Android asks.", html);
        Assert.Contains("Open the file to install, then open NeedleBase.", html);
        Assert.Contains("wa.me/233500785153", html);
        Assert.Contains("Android only", html);
        Assert.Contains("No card is needed to download it.", html);
        Assert.Contains("github.com/Helmut-Essien/NeedleBase/releases", html);
        Assert.Contains("viewport-fit=cover", html);
        Assert.DoesNotContain("nb-sidebar", html);
    }

    [Fact]
    public async Task Anonymous_download_page_lists_stable_and_beta_when_both_exist()
    {
        var links = new AndroidDownloadLinks(StableBuild, BetaBuild, LookupFailed: false, Releases, LatestPage);
        using var factory = Host(links);
        using var client = Client(factory);
        using var response = await client.GetAsync("/download");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        AssertShared(html);
        Assert.Contains("Download NeedleBase 1.0.0", html);
        Assert.Contains(StableBuild.DownloadUrl, html);
        Assert.Contains("needlebase.apk", html);
        Assert.Contains("Download the beta", html);
        Assert.Contains(BetaBuild.DownloadUrl, html);
        Assert.Contains("needlebase-1.0.1-20.apk", html);
        Assert.Contains(">Beta<", html);
        Assert.Contains("This is a test version and may have bugs.", html);
        Assert.DoesNotContain("A stable Android app isn't published yet", html);
        Assert.DoesNotContain("Download NeedleBase 1.0.1 (Beta)", html);
        Assert.DoesNotContain("We couldn't look up the latest app file just now", html);
    }

    [Fact]
    public async Task Anonymous_download_page_offers_beta_when_there_is_no_stable_release()
    {
        var links = new AndroidDownloadLinks(null, CurrentBeta, LookupFailed: false, Releases, LatestPage);
        using var factory = Host(links);
        using var client = Client(factory);
        using var response = await client.GetAsync("/download");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        AssertShared(html);
        Assert.Contains("A stable Android app isn't published yet", html);
        Assert.Contains("Download NeedleBase 0.1.2 (Beta)", html);
        Assert.Contains(CurrentBeta.DownloadUrl, html);
        Assert.Contains("needlebase-0.1.2-18.apk", html);
        Assert.Contains(">Beta<", html);
        Assert.Contains("This is a test version and may have bugs.", html);
        Assert.DoesNotContain("Download NeedleBase 0.1.2<", html);
        Assert.DoesNotContain("We couldn't look up the latest app file just now", html);
    }

    [Fact]
    public async Task Anonymous_download_page_falls_back_to_releases_when_github_lookup_fails()
    {
        var links = new AndroidDownloadLinks(null, null, LookupFailed: true, Releases, LatestPage);
        using var factory = Host(links);
        using var client = Client(factory);
        using var response = await client.GetAsync("/download");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        AssertShared(html);
        Assert.Contains("We couldn't look up the latest app file just now", html);
        Assert.Contains("Open GitHub releases", html);
        Assert.Contains($"href=\"{Releases}\"", html);
        Assert.DoesNotContain("needlebase-0.1.2-18.apk", html);
        Assert.DoesNotContain("Download NeedleBase 0.1.2", html);
        Assert.DoesNotContain("A stable Android app isn't published yet", html);
    }
}
