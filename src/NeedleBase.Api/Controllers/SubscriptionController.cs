using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NeedleBase.Api.Http;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Licensing;
using NeedleBase.Infrastructure.Licensing;

namespace NeedleBase.Api.Controllers;

/// <summary>
/// The shop's NeedleBase license: status for everyone in the shop; the Owner attaches a key (shops created before
/// licensing, or a replacement key). Same Platform checks as registration, with the Owner's own email.
/// </summary>
[ApiController]
[Route("api/subscription")]
[Authorize(Policy = "RequireStaff")]
[AllowWhenReadOnly]
public sealed class SubscriptionController : ControllerBase
{
    private readonly LicenseService _licenses;
    private readonly ICurrentUser _user;

    public SubscriptionController(LicenseService licenses, ICurrentUser user)
    {
        _licenses = licenses;
        _user = user;
    }

    [HttpGet]
    [ProducesResponseType(typeof(SubscriptionStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(_user.BusinessId)
            ? ApiProblems.SessionInvalid(HttpContext)
            : Ok(await _licenses.GetStatusAsync(_user.BusinessId, cancellationToken));

    [HttpPost("license")]
    [Authorize(Policy = "RequireOwner")]
    [EnableRateLimiting(AuthRateLimits.RegisterPolicy)]
    [ProducesResponseType(typeof(SubscriptionStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Attach([FromBody] AttachLicenseRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_user.BusinessId) || string.IsNullOrEmpty(_user.UserId))
        {
            return ApiProblems.SessionInvalid(HttpContext);
        }

        var (status, code, retry) = await _licenses.AttachAsync(_user.BusinessId, _user.UserId, request.LicenseKey, cancellationToken);
        return status is not null ? Ok(status) : ApiProblems.ForLicense(HttpContext, code, retry, "Couldn't add the license key");
    }
}
