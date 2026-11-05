using System.Net.Http.Headers;
using System.Text.Json;

namespace NeedleBase.Api.Updates;

/// <summary>
/// The Android in-app update feed, AssetTag style: the app asks this Api, the Api reads the repository's GitHub
/// Releases (GitHub:Owner / GitHub:Repository, optional GitHub:Token) and returns the <c>update.json</c> attached to the
/// newest release that has one (pre-releases included: 0.x versions are published as pre-releases). The manifest
/// carries the APK's GitHub download address, SHA-256, size and signing certificate, which the app verifies.
/// Answers are cached for 5 minutes (GitHub allows 60 unauthenticated calls an hour); when GitHub is unreachable the
/// last good answer is served for up to an hour.
/// </summary>
public sealed class MobileUpdateFeed
{
    public const string HttpClientName = "github-releases";
    public const string ManifestAssetName = "update.json";
    internal static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan StaleFor = TimeSpan.FromHours(1);
    internal static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);
    private const int MaxManifestBytes = 64 * 1024;

    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<MobileUpdateFeed> _logger;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _json;
    private DateTimeOffset _fetchedAt;
    private DateTimeOffset _nextTry;

    public MobileUpdateFeed(IHttpClientFactory http, IConfiguration config, ILogger<MobileUpdateFeed> logger,
        Func<DateTimeOffset>? now = null)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The current update.json, or null when there is none (not configured, no release, GitHub down).</summary>
    public async Task<string?> GetManifestJsonAsync(CancellationToken ct)
    {
        if (_json is not null && _now() - _fetchedAt < FreshFor)
        {
            return _json;
        }

        await _gate.WaitAsync(ct);
        try
        {
            var now = _now();
            if (_json is not null && now - _fetchedAt < FreshFor)
            {
                return _json;
            }

            if (now >= _nextTry)
            {
                var fresh = await FetchAsync(ct);
                if (fresh is not null)
                {
                    (_json, _fetchedAt) = (fresh, now);
                    return fresh;
                }

                _nextTry = now + RetryAfterFailure;
            }

            return _json is not null && now - _fetchedAt < StaleFor ? _json : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> FetchAsync(CancellationToken ct)
    {
        var owner = _config["GitHub:Owner"]?.Trim();
        var repo = _config["GitHub:Repository"]?.Trim();
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
        {
            _logger.LogWarning("In-app updates: GitHub:Owner / GitHub:Repository are not set");
            return null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var client = _http.CreateClient(HttpClientName);
            using var list = Request($"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases?per_page=30",
                "application/vnd.github+json");
            using var listResponse = await client.SendAsync(list, timeout.Token);
            if (!listResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("In-app updates: GitHub releases answered {Status}", (int)listResponse.StatusCode);
                return null;
            }

            var assetUrl = NewestManifestAsset(await listResponse.Content.ReadAsStringAsync(timeout.Token));
            if (assetUrl is null)
            {
                _logger.LogInformation("In-app updates: no release of {Owner}/{Repo} has {Asset}", owner, repo, ManifestAssetName);
                return null;
            }

            // The asset API (Accept: octet-stream) works for private repositories too when a token is set.
            using var get = Request(assetUrl, "application/octet-stream");
            using var response = await client.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("In-app updates: {Asset} download answered {Status}", ManifestAssetName, (int)response.StatusCode);
                return null;
            }

            // Cap even when Content-Length is missing (a wrong redirect or HTML error page must not be buffered whole).
            if (response.Content.Headers.ContentLength > MaxManifestBytes)
            {
                _logger.LogWarning("In-app updates: {Asset} is larger than {Max} bytes", ManifestAssetName, MaxManifestBytes);
                return null;
            }

            var json = await ReadAtMostAsync(response.Content, MaxManifestBytes, timeout.Token);
            if (json is null)
            {
                _logger.LogWarning("In-app updates: {Asset} is larger than {Max} bytes", ManifestAssetName, MaxManifestBytes);
                return null;
            }

            if (!LooksLikeManifest(json))
            {
                _logger.LogWarning("In-app updates: {Asset} is not a valid manifest", ManifestAssetName);
                return null;
            }

            return json;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning("In-app updates: GitHub unreachable ({Type})", ex.GetType().Name);
            return null;
        }
    }

    private HttpRequestMessage Request(string url, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("NeedleBase-Api");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        var token = _config["GitHub:Token"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }

        return request;
    }

    /// <summary>API url of update.json on the newest published (non-draft) release that has one.</summary>
    internal static string? NewestManifestAsset(string releasesJson)
    {
        using var doc = JsonDocument.Parse(releasesJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return doc.RootElement.EnumerateArray()
            .Where(r => !(r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True))
            .Select(r => (
                Published: r.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String &&
                           DateTimeOffset.TryParse(p.GetString(), out var at) ? at : DateTimeOffset.MinValue,
                Url: r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array
                    ? assets.EnumerateArray()
                        .Where(a => a.TryGetProperty("name", out var n) && n.GetString() == ManifestAssetName)
                        .Select(a => a.TryGetProperty("url", out var u) ? u.GetString() : null)
                        .FirstOrDefault(u => u is not null && u.StartsWith("https://api.github.com/", StringComparison.Ordinal))
                    : null))
            .Where(r => r.Url is not null)
            .OrderByDescending(r => r.Published)
            .Select(r => r.Url)
            .FirstOrDefault();
    }

    /// <summary>Reads at most <paramref name="maxBytes"/> UTF-8 bytes; null if the body is larger.</summary>
    private static async Task<string?> ReadAtMostAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[maxBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total > maxBytes ? null : System.Text.Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static bool LooksLikeManifest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("schema", out _) &&
                   doc.RootElement.TryGetProperty("versionCode", out _) &&
                   doc.RootElement.TryGetProperty("apkUrl", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
