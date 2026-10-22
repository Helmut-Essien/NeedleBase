using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace NeedleBase.Web.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    private readonly IHostEnvironment _environment;

    public ErrorModel(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public bool ShowDevelopmentHints => _environment.IsDevelopment();

    /// <summary>Set by UseStatusCodePagesWithReExecute ("?code=400"); null for an unhandled exception.</summary>
    [BindProperty(SupportsGet = true)] public int? Code { get; set; }

    public string Heading => Code switch
    {
        400 => "That form has expired",
        404 => "Page not found",
        _ => "Something went wrong"
    };

    public string Body => Code switch
    {
        // Antiforgery failures land here: the page was open across a sign-in/out or the cookie expired.
        400 => "The page was open too long or you signed in again in another tab. Go back, refresh the page, then try again.",
        404 => "The link may be old or mistyped.",
        _ => "We couldn’t process that request. Try again in a moment."
    };

    public void OnGet() => OnAny();

    public void OnPost() => OnAny();

    private void OnAny()
    {
        RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }
}
