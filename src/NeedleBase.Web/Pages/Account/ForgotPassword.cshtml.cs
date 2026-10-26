using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Account;

/// <summary>Step 1 of a password reset: email → a code is emailed (same reply whether or not the account exists).</summary>
public class ForgotPasswordModel : PageModel
{
    private readonly IApiAuthService _auth;
    private readonly ILogger<ForgotPasswordModel> _logger;

    public ForgotPasswordModel(IApiAuthService auth, ILogger<ForgotPasswordModel> logger)
    {
        _auth = auth;
        _logger = logger;
    }

    [BindProperty]
    [Required(ErrorMessage = "Enter your email")]
    [EmailAddress(ErrorMessage = "Enter a valid email")]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    public string? ErrorMessage { get; private set; }

    public bool Unavailable { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Unavailable = !await _auth.EmailEnabledAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            if (!await _auth.RequestPasswordResetAsync(Email.Trim(), cancellationToken))
            {
                ErrorMessage = _auth.LastErrorMessage;
                return Page();
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Forgot password failed: Api unreachable");
            ErrorMessage = ApiResult<bool>.OfflineMessage;
            return Page();
        }

        // The email rides in the query string only so step 2 can prefill it; the code never does.
        return RedirectToPage("/Account/ResetPassword", new { email = Email.Trim(), sent = true });
    }
}
