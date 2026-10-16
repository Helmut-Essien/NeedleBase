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
        string? businessPhone)
    {
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
