using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly IApiAuthService _authService;
    private readonly ILogger<RegisterModel> _logger;

    public RegisterModel(IApiAuthService authService, ILogger<RegisterModel> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress]
        [MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        [MaxLength(100)]
        [RegularExpression(@"^(?=.*\d).+$", ErrorMessage = "At least 6 characters, including a number")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        [Display(Name = "First name")]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        [Display(Name = "Surname")]
        public string Surname { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        [Display(Name = "Business name")]
        public string BusinessName { get; set; } = string.Empty;

        [MaxLength(40)]
        [Display(Name = "Business phone (optional)")]
        public string? BusinessPhone { get; set; }
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var request = new RegisterBusinessRequest(
                Input.Email,
                Input.Password,
                Input.FirstName,
                Input.Surname,
                Input.BusinessName,
                string.IsNullOrWhiteSpace(Input.BusinessPhone) ? null : Input.BusinessPhone.Trim());

            var session = await _authService.RegisterBusinessAsync(request, cancellationToken);
            if (session is null)
            {
                ErrorMessage = "Registration failed. Check your details or try a different email.";
                return Page();
            }

            await AuthCookieSignIn.SignInAsync(HttpContext, session, rememberMe: false, _logger);
            return RedirectToPage("/Index");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed for {Email}", Input.Email);
            ErrorMessage = "An unexpected error occurred. Please try again.";
            return Page();
        }
    }
}
