using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Account;

public class LogoutModel : PageModel
{
    private readonly IApiAuthService _authService;

    public LogoutModel(IApiAuthService authService)
    {
        _authService = authService;
    }

    public IActionResult OnGet() => RedirectToPage("/Account/Login");

    public async Task<IActionResult> OnPostAsync()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        var refreshToken = User.FindFirst("RefreshToken")?.Value;
        await _authService.LogoutAsync(refreshToken);
        TokenRefreshHandler.ClearCachedTokens(userId);
        await HttpContext.SignOutAsync(AuthCookieSignIn.CookieScheme);
        return RedirectToPage("/Account/Login");
    }
}
