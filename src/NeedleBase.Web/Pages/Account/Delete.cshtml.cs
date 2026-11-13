using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Account;

/// <summary>
/// Public page for Google Play: anyone can read how to request deletion.
/// A signed-in person can delete their account here after confirming their password.
/// </summary>
public class DeleteModel : PageModel
{
    private readonly INeedleBaseApi _api;
    private readonly ILogger<DeleteModel> _logger;

    public DeleteModel(INeedleBaseApi api, ILogger<DeleteModel> logger)
    {
        _api = api;
        _logger = logger;
    }

    [BindProperty] public DeleteInput Input { get; set; } = new();

    public bool IsSignedIn { get; private set; }
    public bool IsOwner { get; private set; }
    public bool Deleted { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string ConfirmationPhrase => IsOwner ? DeleteAccountConfirmations.OwnerShop : DeleteAccountConfirmations.Staff;

    public sealed class DeleteInput
    {
        [Required(ErrorMessage = "Enter your password"), MaxLength(100), DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Type the confirmation words"), MaxLength(40)]
        public string Confirmation { get; set; } = string.Empty;
    }

    public IActionResult OnGet(bool done = false)
    {
        BindState(done);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        BindState(deleted: false);
        if (!IsSignedIn)
        {
            ErrorMessage = "Sign in first to delete your account from this page, or use the email or WhatsApp contacts below.";
            return Page();
        }

        if (!ModelState.IsValid)
        {
            ErrorMessage = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? "Check the details and try again.";
            return Page();
        }

        var result = await _api.DeleteAccountAsync(new DeleteAccountRequest
        {
            Password = Input.Password,
            Confirmation = Input.Confirmation
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Unreachable
                ? "Can't reach NeedleBase. Check your connection and try again."
                : result.Problem is { } problem
                    ? AuthErrorMessages.ForDelete(problem)
                    : "Couldn't delete the account. Try again.";
            return Page();
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst(ClaimTypes.Email)?.Value;
        TokenRefreshHandler.ClearCachedTokens(userId);
        await HttpContext.SignOutAsync(AuthCookieSignIn.CookieScheme);
        return Redirect("/account/delete?done=1");
    }

    private void BindState(bool deleted)
    {
        Deleted = deleted;
        IsSignedIn = !deleted && User.Identity?.IsAuthenticated == true;
        IsOwner = IsSignedIn && string.Equals(User.FindFirstValue(ClaimTypes.Role), BusinessRole.Owner, StringComparison.OrdinalIgnoreCase);
        ViewData["Title"] = "Delete account";
    }
}
