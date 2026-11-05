using System.Net;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NeedleBase.Api.Updates;

namespace NeedleBase.Api.Tests;

/// <summary>Android in-app updates, AssetTag style: the Api serves update.json from the newest GitHub Release.</summary>
public class MobileUpdateFeedTests(NeedleBaseApiFactory factory) : IClassFixture<NeedleBaseApiFactory>
{
    private const string Manifest = """{"schema":1,"packageName":"com.helmutcode.needlebase","versionCode":42,"apkUrl":"https://github.com/o/r/releases/download/v1.2.0/needlebase-1.2.0-42.apk"}""";

    private static string Releases(params (string Published, bool Draft, string? AssetId)[] releases) =>
        "[" + string.Join(",", releases.Select(r =>
            $$"""{"draft":{{(r.Draft ? "true" : "false")}},"published_at":"{{r.Published}}","assets":[{"name":"x.apk","url":"https://api.github.com/repos/o/r/releases/assets/9"}{{(r.AssetId is null ? "" : $$""",{"name":"update.json","url":"https://api.github.com/repos/o/r/releases/assets/{{r.AssetId}}"}""")}}]}""")) + "]";

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public string ReleasesJson = Releases(("2026-11-04T10:00:00Z", false, "1"));
        public HttpStatusCode Status = HttpStatusCode.OK;
        public readonly List<string> Calls = [];
        public readonly Dictionary<string, string> Assets = new() { ["1"] = Manifest };
        /// <summary>When set, asset downloads use this stream and omit Content-Length (oversized-body test).</summary>
        public Stream? AssetStreamWithoutLength;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            lock (Calls) Calls.Add(url + " " + request.Headers.Accept + " " + request.Headers.Authorization);
            if (Status != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(Status));
            if (!url.Contains("/releases?") && AssetStreamWithoutLength is not null)
            {
                var content = new StreamContent(AssetStreamWithoutLength);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
            var body = url.Contains("/releases?") ? ReleasesJson : Assets.GetValueOrDefault(url[(url.LastIndexOf('/') + 1)..]) ?? "";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }

    private static MobileUpdateFeed Feed(FakeGitHub gh, Func<DateTimeOffset> now, string? owner = "o", string? token = null) =>
        new(new Factory(gh), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GitHub:Owner"] = owner, ["GitHub:Repository"] = "r", ["GitHub:Token"] = token
        }).Build(), NullLogger<MobileUpdateFeed>.Instance, now);

    [Fact]
    public async Task Serves_update_json_of_the_newest_release_that_has_one_skipping_drafts()
    {
        var gh = new FakeGitHub
        {
            ReleasesJson = Releases(("2026-11-01T10:00:00Z", false, "1"), ("2026-11-05T10:00:00Z", true, "3"),
                ("2026-11-04T10:00:00Z", false, "2"), ("2026-11-06T10:00:00Z", false, null)), // newest: server-only release
        };
        gh.Assets["2"] = Manifest.Replace("42", "43");
        var json = await Feed(gh, () => DateTimeOffset.UtcNow).GetManifestJsonAsync(default);
        Assert.Contains("\"versionCode\":43", json);
        Assert.StartsWith("https://api.github.com/repos/o/r/releases?per_page=30", gh.Calls[0]);
        Assert.Contains("application/octet-stream", gh.Calls[1]); // asset API (works for private repos with a token)
    }

    [Fact]
    public async Task Caches_for_five_minutes_then_serves_the_last_good_answer_while_GitHub_is_down_for_up_to_an_hour()
    {
        var gh = new FakeGitHub();
        var now = DateTimeOffset.UtcNow;
        var feed = Feed(gh, () => now);
        Assert.NotNull(await feed.GetManifestJsonAsync(default));
        Assert.NotNull(await feed.GetManifestJsonAsync(default));
        Assert.Equal(2, gh.Calls.Count); // one list + one asset

        gh.Status = HttpStatusCode.Forbidden; // rate limited
        now += TimeSpan.FromMinutes(6);
        Assert.NotNull(await feed.GetManifestJsonAsync(default));
        Assert.NotNull(await feed.GetManifestJsonAsync(default)); // no retry within a minute
        Assert.Equal(3, gh.Calls.Count);

        now += TimeSpan.FromMinutes(60);
        Assert.Null(await feed.GetManifestJsonAsync(default));
    }

    [Fact]
    public async Task Nothing_when_not_configured_no_manifest_or_invalid_manifest()
    {
        Assert.Null(await Feed(new FakeGitHub(), () => DateTimeOffset.UtcNow, owner: "").GetManifestJsonAsync(default));
        Assert.Null(await Feed(new FakeGitHub { ReleasesJson = Releases(("2026-11-04T10:00:00Z", false, null)) }, () => DateTimeOffset.UtcNow).GetManifestJsonAsync(default));
        var bad = new FakeGitHub(); bad.Assets["1"] = "<html>not json</html>";
        Assert.Null(await Feed(bad, () => DateTimeOffset.UtcNow).GetManifestJsonAsync(default));
    }

    [Fact]
    public async Task Sends_the_token_only_when_one_is_set()
    {
        var gh = new FakeGitHub();
        await Feed(gh, () => DateTimeOffset.UtcNow, token: "test-token-not-real").GetManifestJsonAsync(default);
        Assert.All(gh.Calls, c => Assert.EndsWith("Bearer test-token-not-real", c));
        var anon = new FakeGitHub();
        await Feed(anon, () => DateTimeOffset.UtcNow).GetManifestJsonAsync(default);
        Assert.All(anon.Calls, c => Assert.DoesNotContain("Bearer", c));
    }

    [Fact]
    public async Task Oversized_manifest_without_content_length_is_rejected()
    {
        var huge = new MemoryStream(new byte[65 * 1024]); // 1 byte over the 64 KiB cap
        var gh = new FakeGitHub { AssetStreamWithoutLength = huge };
        Assert.Null(await Feed(gh, () => DateTimeOffset.UtcNow).GetManifestJsonAsync(default));
        Assert.Contains(gh.Calls, c => c.Contains("application/octet-stream"));
    }

    [Fact]
    public async Task Endpoint_is_anonymous_and_returns_the_manifest_or_503()
    {
        var gh = new FakeGitHub();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddHttpClient(MobileUpdateFeed.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => gh);
            s.AddSingleton<MobileUpdateFeed>();
        }));
        using var client = app.CreateClient();
        using var ok = await client.GetAsync("/api/mobile/update");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/json", ok.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Manifest, await ok.Content.ReadAsStringAsync());

        var down = new FakeGitHub { Status = HttpStatusCode.ServiceUnavailable };
        using var app2 = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddHttpClient(MobileUpdateFeed.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => down)));
        using var none = await app2.CreateClient().GetAsync("/api/mobile/update");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, none.StatusCode);
    }
}
