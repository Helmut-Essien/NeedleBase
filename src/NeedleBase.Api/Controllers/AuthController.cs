using NeedleBase.Api.Http;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Contracts.Auth;

namespace NeedleBase.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
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

    private string? GetIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();
}
