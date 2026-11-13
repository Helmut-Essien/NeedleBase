using NeedleBase.Api.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Infrastructure.Persistence;
using System.Security.Claims;

namespace NeedleBase.Api.Controllers;

/// <summary>The signed-in person's account and shop (probe plus account deletion).</summary>
[ApiController]
[Route("api/me")]
[AllowWhenReadOnly]
[Authorize(Policy = "RequireStaff")]
public sealed class MeController : ControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly IAuthService _authService;
    private readonly ApplicationDbContext _db;

    public MeController(ICurrentUser currentUser, IAuthService authService, ApplicationDbContext db)
    {
        _currentUser = currentUser;
        _authService = authService;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_currentUser.BusinessId))
        {
            return ApiProblems.SessionInvalid(HttpContext);
        }

        var business = await _db.Businesses.AsNoTracking()
            .Where(b => b.Id == _currentUser.BusinessId)
            .Select(b => new { b.Id, b.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (business is null)
        {
            return ApiProblems.BusinessNotFound(HttpContext);
        }

        return Ok(new
        {
            userId = _currentUser.UserId,
            businessId = _currentUser.BusinessId,
            role = _currentUser.Role,
            businessName = business.Name
        });
    }

    /// <summary>Delete the signed-in account (staff) or close the shop (owner), after password confirmation.</summary>
    [HttpPost("delete-account")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return ApiProblems.SessionInvalid(HttpContext);
        }

        var result = await _authService.DeleteAccountAsync(userId, _currentUser.BusinessId, request, GetIpAddress(), cancellationToken);
        if (result.RetryAfter is { } wait)
        {
            Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return ApiProblems.Create(HttpContext, StatusCodes.Status429TooManyRequests, AuthResult.LoginThrottledCode, "Too many tries", result.Error);
        }

        if (!result.Succeeded)
        {
            var code = result.ErrorCode ?? "UNAUTHORIZED";
            var status = code switch
            {
                StaffErrorMessages.PasswordIncorrect
                    or "DELETE_CONFIRMATION_REQUIRED"
                    or "OWNER_CONFIRMATION_REQUIRED" => StatusCodes.Status400BadRequest,
                "SHOP_CLOSED" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status401Unauthorized
            };
            var title = code switch
            {
                "DELETE_CONFIRMATION_REQUIRED" or "OWNER_CONFIRMATION_REQUIRED" => "Confirmation needed",
                "SHOP_CLOSED" => "This shop account has been deleted",
                _ => "Couldn't delete the account"
            };
            return ApiProblems.Create(HttpContext, status, code, title, result.Error);
        }

        return NoContent();
    }

    private string? GetIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();
}
