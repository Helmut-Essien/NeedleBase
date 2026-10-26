using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Profile;

/// <summary>Change password. Every other browser/phone is signed out; this browser keeps going with new tokens.</summary>
public class PasswordModel : AppPageModel
{
    private readonly INeedleBaseApi _api;
    private readonly ILogger<PasswordModel> _logger;

    public PasswordModel(INeedleBaseApi api, ILogger<PasswordModel> logger)
    {
        _api = api;
        _logger = logger;
    }

    [BindProperty] public PasswordInput Input { get; set; } = new();

    [TempData] public string? Flash { get; set; }

    public string? StatusMessage { get; private set; }

    public sealed class PasswordInput
    {
        [Required(ErrorMessage = "Enter your current password"), MaxLength(100), DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Choose a new password"), MinLength(NeedleBase.Application.Common.PasswordPolicy.MinLength, ErrorMessage = NeedleBase.Application.Common.PasswordPolicy.Hint), MaxLength(NeedleBase.Application.Common.PasswordPolicy.MaxLength), RegularExpression(NeedleBase.Application.Common.PasswordPolicy.Pattern, ErrorMessage = NeedleBase.Application.Common.PasswordPolicy.Hint), DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Type the new password again"), Compare(nameof(NewPassword), ErrorMessage = "The new passwords don't match"), DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet()
    {
        StatusMessage = Flash;
        Loaded = true;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Loaded = true;
        if (!ModelState.IsValid)
        {
            PageError = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
                        ?? "Check the details and try again.";
            CanRetry = false;
            return Page();
        }

        var result = await _api.ChangePasswordAsync(new ChangePasswordRequest
        {
            CurrentPassword = Input.CurrentPassword,
            NewPassword = Input.NewPassword
        }, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            CanRetry = false;
            return Fail(result) ?? Page();
        }

        // Keep this browser signed in with the new tokens (same "remember me" as before).
        var current = await HttpContext.AuthenticateAsync(AuthCookieSignIn.CookieScheme);
        await AuthCookieSignIn.SignInAsync(HttpContext, result.Value.ToSession(), current.Properties?.IsPersistent ?? false, _logger);
        Flash = "Password changed. Other phones and browsers have been signed out.";
        return RedirectToPage();
    }
}
