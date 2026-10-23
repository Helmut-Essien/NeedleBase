using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages;

/// <summary>Shared page state for Api-backed pages: one banner (error/offline), and 401 → login.</summary>
public abstract class AppPageModel : PageModel
{
    /// <summary>Page-level message (offline, forbidden, or an Api error's plain detail).</summary>
    public string? PageError { get; protected set; }

    /// <summary>True when the Api couldn't be reached (offline state copy, Retry button).</summary>
    public bool IsOffline { get; protected set; }

    /// <summary>True after the main data loaded (so "empty" is shown only for a real empty answer).</summary>
    public bool Loaded { get; protected set; }

    /// <summary>False when retrying can't help (no access, not found), so the error hides "Try again".</summary>
    public bool CanRetry { get; protected set; } = true;

    /// <summary>
    /// Records a failed call on the page. Returns a redirect for 401 (session ended), otherwise null so the page
    /// renders its error/offline state.
    /// </summary>
    protected IActionResult? Fail<T>(ApiResult<T> result)
    {
        if (result.Status == HttpStatusCode.Unauthorized)
        {
            var returnUrl = Request.Path + Request.QueryString;
            return LocalRedirect($"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        IsOffline = result.Unreachable;
        CanRetry = result.Status is not (HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        PageError = result.Status == HttpStatusCode.Forbidden && string.IsNullOrWhiteSpace(result.Problem?.Detail)
            ? "You don't have access to this."
            : result.Message;
        return null;
    }
}
