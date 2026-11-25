using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using NeedleBase.Mobile.Configuration;
using NeedleBase.Mobile.Data.Sync;

namespace NeedleBase.Mobile.Services;

/// <summary>Owner: download the shop JSON export from the Api (works while read-only).</summary>
public sealed class ShopExportApi
{
    private readonly IHttpClientFactory _factory;
    private readonly ApiSettings _settings;

    public ShopExportApi(IHttpClientFactory factory, IOptions<ApiSettings> settings)
    {
        _factory = factory;
        _settings = settings.Value;
    }

    public async Task<(byte[]? Bytes, string? FileName, string? Error)> DownloadAsync(
        CancellationToken cancellationToken = default)
    {
        var client = _factory.CreateClient(HttpSyncApi.ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(_settings.BaseUrl), "api/export"));
        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (null, null, response.StatusCode == System.Net.HttpStatusCode.Forbidden
                    ? "Export isn't available on this plan."
                    : "Couldn't download the export. Check your connection and try again.");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var name = SafeFileName(ContentFileName(response.Content.Headers.ContentDisposition))
                ?? $"needlebase-export-{DateTime.UtcNow:yyyyMMdd}.json";
            return (bytes, name, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (null, null, "Couldn't download the export. Check your connection and try again.");
        }
    }

    private static string? ContentFileName(ContentDispositionHeaderValue? disposition)
    {
        if (disposition is null)
        {
            return null;
        }

        var name = disposition.FileNameStar ?? disposition.FileName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return name.Trim().Trim('"');
    }

    /// <summary>Basename only — never write a Content-Disposition path segment into CacheDirectory.</summary>
    public static string? SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var leaf = Path.GetFileName(name.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(leaf) || leaf is "." or "..")
        {
            return null;
        }

        return leaf;
    }
}
