using NeedleBase.Api.Http;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Errors;

namespace NeedleBase.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IEmailSender _email;

    public AuthController(IAuthService authService, IEmailSender email)
    {
        _authService = authService;
        _email = email;
    }

    /// <summary>What sign-in screens may offer (Forgot password only when email is configured).</summary>
    [HttpGet("options")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthOptionsDto), StatusCodes.Status200OK)]
    public IActionResult Options() => Ok(new AuthOptionsDto(_email.IsConfigured));

    /// <summary>Emails a reset code if the address has an account. Always 202 (never says whether it does).</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.RequestPasswordResetAsync(request, GetIpAddress(), cancellationToken);
        if (result.RetryAfter is { } wait)
        {
            return Throttled(wait, "Too many requests from here. Wait a few minutes, then try again.");
        }

        return result.Succeeded ? Accepted() : ApiProblems.ForStaff(HttpContext, result.ErrorCode);
    }

    /// <summary>Sets a new password with the emailed code; every device is signed out (sign in again).</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.ResetPasswordAsync(request, GetIpAddress(), cancellationToken);
        if (result.RetryAfter is { } wait)
        {
            return Throttled(wait, "Too many wrong codes from here. Wait a moment, then try again.");
        }

        return result.Succeeded ? NoContent() : ApiProblems.ForStaff(HttpContext, result.ErrorCode);
    }

    /// <summary>Register a new business and its Owner account.</summary>
    [HttpPost("register-business")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegisterBusiness(
        [FromBody] RegisterBusinessRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterBusinessAsync(request, GetIpAddress(), cancellationToken);
        if (!result.Succeeded || result.Tokens is null || result.User is null)
        {
            return ApiProblems.Create(HttpContext, StatusCodes.Status400BadRequest, result.ErrorCode ?? "REGISTER_FAILED", "Couldn't create your account", result.Error);
        }

        return Ok(new
        {
            accessToken = result.Tokens.AccessToken,
            refreshToken = result.Tokens.RefreshToken,
            user = result.User
        });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request, GetIpAddress(), cancellationToken);
        if (result.RetryAfter is { } wait)
        {
            Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return ApiProblems.Create(HttpContext, StatusCodes.Status429TooManyRequests, AuthResult.LoginThrottledCode, "Too many tries", result.Error);
        }

        if (!result.Succeeded || result.Tokens is null || result.User is null)
        {
            return ApiProblems.Create(HttpContext, StatusCodes.Status401Unauthorized, result.ErrorCode ?? "UNAUTHORIZED", "Couldn't sign you in", result.Error);
        }

        return Ok(new
        {
            accessToken = result.Tokens.AccessToken,
            refreshToken = result.Tokens.RefreshToken,
            user = result.User
        });
    }

    [HttpPost("refresh-token")]
    [EnableRateLimiting(AuthRateLimits.RefreshPolicy)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.RefreshAsync(request.RefreshToken, GetIpAddress(), cancellationToken);
        if (!result.Succeeded || result.Tokens is null)
        {
            return ApiProblems.Create(HttpContext, StatusCodes.Status401Unauthorized, result.ErrorCode ?? "UNAUTHORIZED", "Couldn't sign you in", result.Error);
        }

        return Ok(new
        {
            accessToken = result.Tokens.AccessToken,
            refreshToken = result.Tokens.RefreshToken,
            user = result.User
        });
    }

    /// <summary>Revoke the current refresh token (and clear client session on callers).</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest? request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue("sub")
                     ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _authService.LogoutAsync(request?.RefreshToken, userId, GetIpAddress(), cancellationToken);
        return NoContent();
    }

    /// <summary>Who an invite code is for (business, role, email). POST so the code stays out of URLs and logs.</summary>
    [HttpPost("invite-preview")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(NeedleBase.Application.Contracts.Staff.InvitePreviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> InvitePreview([FromBody] InviteCodeRequest request, CancellationToken cancellationToken)
    {
        var (preview, code, wait) = await _authService.PreviewInviteAsync(request.Code, GetIpAddress(), cancellationToken);
        if (wait is { } w)
        {
            return Throttled(w, "Too many wrong codes from here. Wait a moment, then try again.");
        }

        return preview is null ? ApiProblems.ForStaff(HttpContext, code) : Ok(preview);
    }

    /// <summary>Join a business with an invite code: creates the account and signs in.</summary>
    [HttpPost("accept-invite")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.AcceptInviteAsync(request, GetIpAddress(), cancellationToken);
        if (result.RetryAfter is { } wait)
        {
            return Throttled(wait, "Too many wrong codes from here. Wait a moment, then try again.");
        }

        if (!result.Succeeded || result.Tokens is null || result.User is null)
        {
            return ApiProblems.ForStaff(HttpContext, result.ErrorCode);
        }

        return Ok(new { accessToken = result.Tokens.AccessToken, refreshToken = result.Tokens.RefreshToken, user = result.User });
    }

    /// <summary>Change your password. Signs out every other device; returns fresh tokens for this one.</summary>
    [HttpPost("change-password")]
    [Authorize(Policy = "RequireStaff")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return ApiProblems.SessionInvalid(HttpContext);
        }

        var result = await _authService.ChangePasswordAsync(userId, request, GetIpAddress(), cancellationToken);
        if (result.RetryAfter is { } wait)
        {
            return Throttled(wait, result.Error);
        }

        if (!result.Succeeded || result.Tokens is null || result.User is null)
        {
            return result.ErrorCode is StaffErrorMessages.PasswordIncorrect or StaffErrorMessages.PasswordWeak
                ? ApiProblems.ForStaff(HttpContext, result.ErrorCode)
                : ApiProblems.Create(HttpContext, StatusCodes.Status401Unauthorized, result.ErrorCode ?? "UNAUTHORIZED", "Couldn't change your password", result.Error);
        }

        return Ok(new { accessToken = result.Tokens.AccessToken, refreshToken = result.Tokens.RefreshToken, user = result.User });
    }

    private ObjectResult Throttled(TimeSpan wait, string? detail)
    {
        Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ApiProblems.Create(HttpContext, StatusCodes.Status429TooManyRequests, AuthResult.LoginThrottledCode, "Too many tries", detail);
    }

    private string? GetIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();
}
