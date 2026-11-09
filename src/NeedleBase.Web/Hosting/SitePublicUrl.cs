namespace NeedleBase.Web.Hosting;

/// <summary>Absolute public site origin (https://host/) for canonical URLs, Open Graph and the sitemap.</summary>
public static class SitePublicUrl
{
    /// <summary>
    /// Prefer FrontendBaseUrl (written from SITE_URL on deploy), then Api:BaseUrl (same value on the one-app site).
    /// Null when unset or not an absolute http(s) URL (local smoke without SITE_URL).
    /// </summary>
    public static string? Get(IConfiguration configuration)
    {
        foreach (var key in new[] { "FrontendBaseUrl", "Api:BaseUrl" })
        {
            var raw = configuration[key]?.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                continue;
            }

            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                continue;
            }

            var builder = new UriBuilder(uri) { Path = "/", Query = "", Fragment = "" };
            return builder.Uri.AbsoluteUri;
        }

        return null;
    }

    public static string Combine(string? origin, string path)
    {
        path = path.StartsWith('/') ? path : "/" + path;
        if (string.IsNullOrEmpty(origin))
        {
            return path;
        }

        return origin.TrimEnd('/') + path;
    }
}
