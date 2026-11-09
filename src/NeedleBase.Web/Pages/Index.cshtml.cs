using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeedleBase.Web.Hosting;
using NeedleBase.Web.Marketing;

namespace NeedleBase.Web.Pages;

/// <summary>Public marketing landing at /. Signed-in users go straight to the dashboard.</summary>
[AllowAnonymous]
public class IndexModel(IConfiguration configuration) : PageModel
{
    public const string AndroidLatestReleaseUrl = "https://github.com/Helmut-Essien/NeedleBase/releases/latest";

    public string? SiteOrigin { get; private set; }
    public string CanonicalUrl { get; private set; } = "/";
    public string OgImageUrl { get; private set; } = "/images/og-needlebase.png";
    public string CspNonce { get; private set; } = "";

    public string WhatsAppUrl { get; private set; } = LandingSalesInfo.WhatsAppUrl();
    public string MailtoUrl { get; private set; } = LandingSalesInfo.MailtoUrl();
    public string WhatsAppDisplay => LandingSalesInfo.WhatsAppDisplay;
    public string Email => LandingSalesInfo.Email;
    public IReadOnlyList<LandingPlan> Plans => LandingSalesInfo.Plans;

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Dashboard/Index");
        }

        SiteOrigin = SitePublicUrl.Get(configuration);
        CanonicalUrl = SitePublicUrl.Combine(SiteOrigin, "/");
        OgImageUrl = SitePublicUrl.Combine(SiteOrigin, "/images/og-needlebase.png");
        CspNonce = HttpContext.Items["CspNonce"] as string ?? "";
        WhatsAppUrl = LandingSalesInfo.WhatsAppUrl();
        MailtoUrl = LandingSalesInfo.MailtoUrl();
        return Page();
    }
}
