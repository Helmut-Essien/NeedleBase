using System.ComponentModel.DataAnnotations;

namespace NeedleBase.Application.Contracts.Auth;

public sealed class RegisterBusinessRequest
{
    public RegisterBusinessRequest()
    {
    }

    public RegisterBusinessRequest(
        string email,
        string password,
        string firstName,
        string surname,
        string businessName,
        string? businessPhone,
        string? licenseKey = null)
    {
        LicenseKey = licenseKey;
        Email = email;
        Password = password;
        FirstName = firstName;
        Surname = surname;
        BusinessName = businessName;
        BusinessPhone = businessPhone;
    }

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(6), MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Surname { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string BusinessName { get; set; } = string.Empty;

    /// <summary>Platform license key from the welcome email (required unless <c>Platform:RequireLicenseKey</c> is false).</summary>
    [MaxLength(200)]
    public string? LicenseKey { get; set; }

    [MaxLength(40)]
    public string? BusinessPhone { get; set; }
}

public sealed class LoginRequest
{
    public LoginRequest()
    {
    }

    public LoginRequest(string email, string password)
    {
        Email = email;
        Password = password;
    }

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public sealed record TokenResponse(string AccessToken, string RefreshToken);

public sealed class RefreshTokenRequest
{
    public RefreshTokenRequest()
    {
    }

    public RefreshTokenRequest(string accessToken, string refreshToken)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
    }

    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class LogoutRequest
{
    public LogoutRequest()
    {
    }

    public LogoutRequest(string? refreshToken)
    {
        RefreshToken = refreshToken;
    }

    public string? RefreshToken { get; set; }
}

public sealed record AuthUserDto(
    string UserId,
    string Email,
    string FirstName,
    string Surname,
    string BusinessId,
    string BusinessName,
    string Role);

public sealed class InviteCodeRequest
{
    [Required, MaxLength(40)]
    public string Code { get; set; } = string.Empty;
}

public sealed class AcceptInviteRequest
{
    [Required, MaxLength(40)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Surname { get; set; } = string.Empty;

    [Required, MinLength(6), MaxLength(100)]
    public string Password { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required, MaxLength(100)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(6), MaxLength(100)]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class ForgotPasswordRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required, MinLength(6), MaxLength(100)]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>What the sign-in screens may offer: Forgot password only when the server can send email.</summary>
public sealed record AuthOptionsDto(bool EmailEnabled);

/// <summary>Typed confirmation phrases required to delete an account (trimmed, case-insensitive on the server).</summary>
public static class DeleteAccountConfirmations
{
    public const string Staff = "DELETE";
    public const string OwnerShop = "DELETE SHOP";

    /// <summary>Case-insensitive; leading, trailing and repeated whitespace are ignored.</summary>
    public static bool Matches(string? actual, string expected)
    {
        if (string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        var normalized = string.Join(' ', actual.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return string.Equals(normalized, expected, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class DeleteAccountRequest
{
    [Required, MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Staff type DELETE. An Owner must type DELETE SHOP, because that closes the whole shop.</summary>
    [Required, MaxLength(40)]
    public string Confirmation { get; set; } = string.Empty;
}
