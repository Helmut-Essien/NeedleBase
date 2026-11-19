using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeedleBase.Web.Downloads;
using NeedleBase.Web.Hosting;
using NeedleBase.Web.Marketing;

namespace NeedleBase.Web.Pages;

/// <summary>Public Android download page at /download. Does not redirect signed-in users.</summary>
[AllowAnonymous]
public class DownloadModel(IAndroidReleaseCatalog catalog, IConfiguration configuration) : PageModel
{
    public string CanonicalUrl { get; private set; } = "/download";
    public string OgImageUrl { get; private set; } = "/images/og-needlebase.png";
    public string CspNonce { get; private set; } = "";
    public AndroidDownloadLinks Links { get; private set; } =
        new(null, null, LookupFailed: true, "https://github.com/Helmut-Essien/NeedleBase/releases",
            "https://github.com/Helmut-Essien/NeedleBase/releases/latest");
    public string WhatsAppUrl { get; private set; } = LandingSalesInfo.WhatsAppHelpUrl();
    public string WhatsAppDisplay => LandingSalesInfo.WhatsAppDisplay;

    public AndroidBuild? Stable => Links.Stable;
    public AndroidBuild? Beta => Links.Beta;
    public bool LookupFailed => Links.LookupFailed;
    public string ReleasesUrl => Links.ReleasesUrl;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var origin = SitePublicUrl.Get(configuration);
        CanonicalUrl = SitePublicUrl.Combine(origin, "/download");
        OgImageUrl = SitePublicUrl.Combine(origin, "/images/og-needlebase.png");
        CspNonce = HttpContext.Items["CspNonce"] as string ?? "";
        WhatsAppUrl = LandingSalesInfo.WhatsAppHelpUrl();
        Links = await catalog.GetAsync(cancellationToken);
    }

    public static string FileMeta(AndroidBuild build)
    {
        var size = GitHubAndroidReleaseCatalog.FormatSize(build.SizeBytes);
        return $"The file is {build.FileName}{size}.";
    }
}
