using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Account;

/// <summary>Step 2: email + emailed code + new password. Every device is signed out; then sign in again.</summary>
public class ResetPasswordModel : PageModel
{
    private readonly IApiAuthService _auth;
    private readonly ILogger<ResetPasswordModel> _logger;

    public ResetPasswordModel(IApiAuthService auth, ILogger<ResetPasswordModel> logger)
    {
        _auth = auth;
        _logger = logger;
    }

    [BindProperty] public ResetInput Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public bool Sent { get; private set; }

    [TempData] public string? Notice { get; set; }

    public sealed class ResetInput
    {
        [Required(ErrorMessage = "Enter your email"), EmailAddress(ErrorMessage = "Enter a valid email"), MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the code from the email"), MaxLength(20)]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Choose a new password")]
        [MinLength(PasswordPolicy.MinLength, ErrorMessage = PasswordPolicy.Hint)]
        [MaxLength(PasswordPolicy.MaxLength)]
        [RegularExpression(PasswordPolicy.Pattern, ErrorMessage = PasswordPolicy.Hint)]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Type the new password again"), Compare(nameof(NewPassword), ErrorMessage = "The new passwords don't match")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet(string? email = null, bool sent = false)
    {
        Input.Email = email?.Trim() ?? string.Empty;
        Sent = sent;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var ok = await _auth.ResetPasswordAsync(new ResetPasswordRequest
            {
                Email = Input.Email.Trim(),
                Code = Input.Code.Trim(),
                NewPassword = Input.NewPassword
            }, cancellationToken);
            if (!ok)
            {
                ErrorMessage = _auth.LastErrorMessage;
                return Page();
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Reset password failed: Api unreachable");
            ErrorMessage = ApiResult<bool>.OfflineMessage;
            return Page();
        }

        Notice = "Password changed. Sign in with your new password. Other phones and browsers were signed out.";
        return RedirectToPage("/Account/Login");
    }
}
