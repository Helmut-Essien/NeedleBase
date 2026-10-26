using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Account;

/// <summary>Join a business with an invite code: check the code (shop + role), then name + password → signed in.</summary>
public class AcceptInviteModel : PageModel
{
    private readonly IApiAuthService _auth;
    private readonly ILogger<AcceptInviteModel> _logger;

    public AcceptInviteModel(IApiAuthService auth, ILogger<AcceptInviteModel> logger)
    {
        _auth = auth;
        _logger = logger;
    }

    [BindProperty]
    [Required(ErrorMessage = "Enter the invite code")]
    [MaxLength(40)]
    public string Code { get; set; } = string.Empty;

    [BindProperty] public JoinInput Join { get; set; } = new();

    public InvitePreviewDto? Preview { get; private set; }

    public string? ErrorMessage { get; private set; }

    public sealed class JoinInput
    {
        [Required(ErrorMessage = "First name is required"), MaxLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Surname is required"), MaxLength(100)]
        public string Surname { get; set; } = string.Empty;

        [Required(ErrorMessage = "Choose a password")]
        [MinLength(NeedleBase.Application.Common.PasswordPolicy.MinLength, ErrorMessage = NeedleBase.Application.Common.PasswordPolicy.Hint)]
        [MaxLength(NeedleBase.Application.Common.PasswordPolicy.MaxLength)]
        [RegularExpression(NeedleBase.Application.Common.PasswordPolicy.Pattern, ErrorMessage = NeedleBase.Application.Common.PasswordPolicy.Hint)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostCheckAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        if (string.IsNullOrWhiteSpace(Code))
        {
            ErrorMessage = "Enter the invite code.";
            return Page();
        }

        Preview = await _auth.PreviewInviteAsync(Code.Trim(), cancellationToken);
        ErrorMessage = Preview is null ? _auth.LastErrorMessage : null;
        if (Preview?.Name is { } name && string.IsNullOrEmpty(Join.FirstName))
        {
            var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Join.FirstName = parts.ElementAtOrDefault(0) ?? string.Empty;
            Join.Surname = parts.ElementAtOrDefault(1) ?? string.Empty;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostJoinAsync(CancellationToken cancellationToken)
    {
        Preview = await _auth.PreviewInviteAsync(Code.Trim(), cancellationToken);
        if (Preview is null)
        {
            ErrorMessage = _auth.LastErrorMessage;
            return Page();
        }

        if (!ModelState.IsValid)
        {
            ErrorMessage = "Please correct the errors and try again.";
            return Page();
        }

        try
        {
            var session = await _auth.AcceptInviteAsync(new AcceptInviteRequest
            {
                Code = Code.Trim(),
                FirstName = Join.FirstName.Trim(),
                Surname = Join.Surname.Trim(),
                Password = Join.Password
            }, cancellationToken);
            if (session is null)
            {
                ErrorMessage = _auth.LastErrorMessage ?? "Couldn't join. Try again.";
                return Page();
            }

            await AuthCookieSignIn.SignInAsync(HttpContext, session, rememberMe: false, _logger);
            return RedirectToPage("/Index");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Accept invite failed: Api unreachable");
            ErrorMessage = ApiResult<bool>.OfflineMessage;
            return Page();
        }
    }
}
