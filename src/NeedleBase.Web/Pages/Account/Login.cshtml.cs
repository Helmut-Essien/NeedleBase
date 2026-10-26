using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Account;

public class LoginModel : PageModel
{
    private readonly IApiAuthService _authService;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(IApiAuthService authService, ILogger<LoginModel> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }

    /// <summary>One-time note after a password reset.</summary>
    [TempData] public string? Notice { get; set; }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            ErrorMessage = "Please correct the errors and try again.";
            return Page();
        }

        try
        {
            var session = await _authService.LoginAsync(
                new LoginRequest(Input.Email, Input.Password),
                cancellationToken);

            if (session is null)
            {
                ErrorMessage = _authService.LastErrorMessage ?? "Invalid email or password.";
                return Page();
            }

            await AuthCookieSignIn.SignInAsync(HttpContext, session, Input.RememberMe, _logger);
            return RedirectToLocal(returnUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during login for {Email}", Input.Email);
            ErrorMessage = "An unexpected error occurred. Please try again.";
            return Page();
        }
    }

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToPage("/Index");
    }
}
