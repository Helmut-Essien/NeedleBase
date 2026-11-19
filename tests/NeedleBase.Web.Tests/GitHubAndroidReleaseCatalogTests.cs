using NeedleBase.Web.Downloads;

namespace NeedleBase.Web.Tests;

/// <summary>Parsing of GitHub Releases JSON for the /download stable and beta APKs.</summary>
public class GitHubAndroidReleaseCatalogTests
{
    private const string Owner = "Helmut-Essien";
    private const string Repo = "NeedleBase";

    private static string Release(string tag, string name, bool prerelease, bool draft, params (string File, string Url, long Size)[] assets)
    {
        var assetJson = string.Join(",", assets.Select(a =>
            $$"""{"name":"{{a.File}}","browser_download_url":"{{a.Url}}","size":{{a.Size}}}"""));
        var pre = prerelease ? "true" : "false";
        var d = draft ? "true" : "false";
        return $$"""{"tag_name":"{{tag}}","name":"{{name}}","draft":{{d}},"prerelease":{{pre}},"html_url":"https://github.com/{{Owner}}/{{Repo}}/releases/tag/{{tag}}","assets":[{{assetJson}}]}""";
    }

    [Fact]
    public void Parse_picks_newest_stable_and_beta_and_skips_drafts()
    {
        var json = "[" + string.Join(",",
            Release("mobile-v1.0.1", "NeedleBase Android 1.0.1", prerelease: true, draft: false,
                ("needlebase-1.0.1-20.apk", "https://github.com/Helmut-Essien/NeedleBase/releases/download/mobile-v1.0.1/needlebase-1.0.1-20.apk", 65_100_000)),
            Release("mobile-v1.0.0-draft", "hidden", prerelease: false, draft: true,
                ("needlebase.apk", "https://github.com/Helmut-Essien/NeedleBase/releases/download/mobile-v1.0.0-draft/needlebase.apk", 1)),
            Release("mobile-v1.0.0", "NeedleBase Android 1.0.0", prerelease: false, draft: false,
                ("needlebase-1.0.0-19.apk", "https://github.com/Helmut-Essien/NeedleBase/releases/download/mobile-v1.0.0/needlebase-1.0.0-19.apk", 65_000_000),
                ("needlebase.apk", "https://github.com/Helmut-Essien/NeedleBase/releases/download/mobile-v1.0.0/needlebase.apk", 65_000_000))
        ) + "]";

        var links = GitHubAndroidReleaseCatalog.Parse(json, Owner, Repo);
        Assert.False(links.LookupFailed);
        Assert.NotNull(links.Stable);
        Assert.Equal("1.0.0", links.Stable.Version);
        Assert.Equal("needlebase.apk", links.Stable.FileName);
        Assert.Equal("https://github.com/Helmut-Essien/NeedleBase/releases/download/mobile-v1.0.0/needlebase.apk", links.Stable.DownloadUrl);
        Assert.NotNull(links.Beta);
        Assert.Equal("1.0.1", links.Beta.Version);
        Assert.Equal("needlebase-1.0.1-20.apk", links.Beta.FileName);
        Assert.Equal("https://github.com/Helmut-Essien/NeedleBase/releases", links.ReleasesUrl);
        Assert.Equal("https://github.com/Helmut-Essien/NeedleBase/releases/latest", links.LatestStablePageUrl);
    }

    [Fact]
    public void Parse_with_only_prereleases_has_no_stable()
    {
        var json = "[" + Release("mobile-v0.1.2", "NeedleBase Android 0.1.2", prerelease: true, draft: false,
            ("needlebase-0.1.2-18.apk", "https://github.com/Helmut-Essien/NeedleBase/releases/download/mobile-v0.1.2/needlebase-0.1.2-18.apk", 64_742_777)) + "]";

        var links = GitHubAndroidReleaseCatalog.Parse(json, Owner, Repo);
        Assert.False(links.LookupFailed);
        Assert.Null(links.Stable);
        Assert.NotNull(links.Beta);
        Assert.Equal("0.1.2", links.Beta.Version);
        Assert.Equal("needlebase-0.1.2-18.apk", links.Beta.FileName);
    }

    [Fact]
    public void Parse_non_array_is_a_failed_lookup()
    {
        var links = GitHubAndroidReleaseCatalog.Parse("""{"message":"API error"}""", Owner, Repo);
        Assert.True(links.LookupFailed);
        Assert.Null(links.Stable);
        Assert.Null(links.Beta);
    }

    [Fact]
    public void VersionFrom_reads_mobile_and_plain_tags()
    {
        Assert.Equal("0.1.2", GitHubAndroidReleaseCatalog.VersionFrom("mobile-v0.1.2", "NeedleBase Android 0.1.2"));
        Assert.Equal("1.0.0", GitHubAndroidReleaseCatalog.VersionFrom("v1.0.0", null));
        Assert.Equal("2.3.4", GitHubAndroidReleaseCatalog.VersionFrom(null, "NeedleBase Android 2.3.4"));
    }

    [Fact]
    public void FormatSize_rounds_bytes_to_whole_megabytes()
    {
        Assert.Equal("", GitHubAndroidReleaseCatalog.FormatSize(null));
        Assert.Equal("", GitHubAndroidReleaseCatalog.FormatSize(0));
        Assert.Equal(" (about 62 MB)", GitHubAndroidReleaseCatalog.FormatSize(64_742_777));
    }
}
