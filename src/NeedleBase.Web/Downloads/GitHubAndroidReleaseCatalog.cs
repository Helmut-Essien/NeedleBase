using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace NeedleBase.Web.Downloads;

/// <summary>
/// Resolves the latest stable and pre-release Android APKs from GitHub Releases.
/// Cached for 12 minutes; a last-good answer is kept for up to an hour if GitHub is unreachable.
/// Uses no token by default; an optional <c>GitHub:Token</c> may be set in config (never committed).
/// </summary>
public sealed class GitHubAndroidReleaseCatalog : IAndroidReleaseCatalog
{
    public const string HttpClientName = "github-releases";
    public const string StableApkFileName = "needlebase.apk";
    internal static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(12);
    internal static readonly TimeSpan StaleFor = TimeSpan.FromHours(1);

    private const string CacheKey = "android-release-catalog";
    private static readonly Regex VersionFromTag = new(@"^(?:mobile-)?v?(\d+\.\d+\.\d+)$", RegexOptions.CultureInvariant);

    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GitHubAndroidReleaseCatalog> _logger;
    private readonly Func<DateTimeOffset> _now;

    public GitHubAndroidReleaseCatalog(
        IHttpClientFactory http,
        IConfiguration config,
        IMemoryCache cache,
        ILogger<GitHubAndroidReleaseCatalog> logger,
        Func<DateTimeOffset>? now = null)
    {
        _http = http;
        _config = config;
        _cache = cache;
        _logger = logger;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<AndroidDownloadLinks> GetAsync(CancellationToken cancellationToken = default)
    {
        var fallback = FallbackLinks();
        if (_cache.TryGetValue(CacheKey, out CacheEntry? entry) && entry is not null)
        {
            if (_now() - entry.FetchedAt < FreshFor)
            {
                return entry.Links;
            }
        }

        try
        {
            var fresh = await FetchAsync(fallback, cancellationToken);
            _cache.Set(CacheKey, new CacheEntry(fresh, _now()), StaleFor);
            return fresh;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            _logger.LogWarning(ex, "Android download catalog: GitHub unreachable ({Type})", ex.GetType().Name);
            if (entry is not null && _now() - entry.FetchedAt < StaleFor && !entry.Links.LookupFailed)
            {
                return entry.Links;
            }

            return fallback with { LookupFailed = true };
        }
    }

    private async Task<AndroidDownloadLinks> FetchAsync(AndroidDownloadLinks fallback, CancellationToken ct)
    {
        var (owner, repo) = OwnerAndRepo();
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
        {
            _logger.LogWarning("Android download catalog: GitHub:Owner / GitHub:Repository are not set");
            return fallback with { LookupFailed = true };
        }

        var client = _http.CreateClient(HttpClientName);
        var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases?per_page=30";
        using var request = CreateRequest(url);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Android download catalog: GitHub releases answered {Status}", (int)response.StatusCode);
            return fallback with { LookupFailed = true };
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return Parse(json, owner, repo);
    }

    /// <summary>Picks the newest non-draft stable and pre-release APKs from a GitHub releases list JSON.</summary>
    internal static AndroidDownloadLinks Parse(string json, string owner, string repo)
    {
        var fallback = LinksFor(owner, repo);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return fallback with { LookupFailed = true };
        }

        AndroidBuild? stable = null;
        AndroidBuild? beta = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            var isPre = release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True;
            var build = TryReadBuild(release, preferStableAlias: !isPre);
            if (build is null)
            {
                continue;
            }

            if (isPre)
            {
                beta ??= build;
            }
            else
            {
                stable ??= build;
            }

            if (stable is not null && beta is not null)
            {
                break;
            }
        }

        return fallback with { Stable = stable, Beta = beta, LookupFailed = false };
    }

    internal static string? VersionFrom(string? tag, string? name)
    {
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var m = VersionFromTag.Match(tag.Trim());
            if (m.Success)
            {
                return m.Groups[1].Value;
            }
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var m = Regex.Match(name, @"(\d+\.\d+\.\d+)", RegexOptions.CultureInvariant);
            if (m.Success)
            {
                return m.Groups[1].Value;
            }
        }

        return string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();
    }

    internal static string FormatSize(long? bytes)
    {
        if (bytes is not > 0)
        {
            return "";
        }

        var mb = Math.Max(1, (int)Math.Round(bytes.Value / (1024d * 1024d), MidpointRounding.AwayFromZero));
        return $" (about {mb.ToString(CultureInfo.InvariantCulture)} MB)";
    }

    private HttpRequestMessage CreateRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("NeedleBase-Web");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        var token = _config["GitHub:Token"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }

        return request;
    }

    private AndroidDownloadLinks FallbackLinks()
    {
        var (owner, repo) = OwnerAndRepo();
        return LinksFor(string.IsNullOrEmpty(owner) ? "Helmut-Essien" : owner,
            string.IsNullOrEmpty(repo) ? "NeedleBase" : repo);
    }

    private (string? Owner, string? Repo) OwnerAndRepo() =>
        (_config["GitHub:Owner"]?.Trim(), _config["GitHub:Repository"]?.Trim());

    private static AndroidDownloadLinks LinksFor(string owner, string repo)
    {
        var root = $"https://github.com/{owner}/{repo}/releases";
        return new AndroidDownloadLinks(null, null, LookupFailed: false, ReleasesUrl: root, LatestStablePageUrl: $"{root}/latest");
    }

    private static AndroidBuild? TryReadBuild(JsonElement release, bool preferStableAlias)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? chosen = null;
        JsonElement? alias = null;
        JsonElement? anyApk = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrEmpty(name) || !name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            anyApk ??= asset;
            if (string.Equals(name, StableApkFileName, StringComparison.OrdinalIgnoreCase))
            {
                alias = asset;
            }
        }

        if (preferStableAlias && alias is not null)
        {
            chosen = alias;
        }
        else
        {
            // Prefer a versioned APK when both exist, so the filename shows the version.
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (!string.IsNullOrEmpty(name)
                    && name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, StableApkFileName, StringComparison.OrdinalIgnoreCase))
                {
                    chosen = asset;
                    break;
                }
            }

            chosen ??= alias ?? anyApk;
        }

        // When only the alias exists on a stable release, still use it.
        chosen ??= alias ?? anyApk;
        if (chosen is null)
        {
            return null;
        }

        var file = chosen.Value.TryGetProperty("name", out var fileNameEl) ? fileNameEl.GetString() : null;
        var url = chosen.Value.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() : null;
        if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.Ordinal))
        {
            return null;
        }

        long? size = null;
        if (chosen.Value.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var bytes) && bytes > 0)
        {
            size = bytes;
        }

        var tag = release.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
        var title = release.TryGetProperty("name", out var titleEl) ? titleEl.GetString() : null;
        var version = VersionFrom(tag, title);
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(tag))
        {
            return null;
        }

        var htmlUrl = release.TryGetProperty("html_url", out var htmlEl) ? htmlEl.GetString() : null;
        return new AndroidBuild(version, tag, file, url, size, htmlUrl);
    }

    private sealed record CacheEntry(AndroidDownloadLinks Links, DateTimeOffset FetchedAt);
}
