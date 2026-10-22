using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Api.Http;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Audit;

namespace NeedleBase.Api.Controllers;

/// <summary>
/// Activity log (read only; there is no update or delete endpoint by design). Owner and Manager; Managers don't see
/// sign-in/security events or IPs. Tailors get 403.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireManagerOrOwner")]
[Route("api/audit")]
public sealed class AuditController : ControllerBase
{
    private readonly IAuditQueryService _audit;
    private readonly ICurrentUser _currentUser;

    public AuditController(IAuditQueryService audit, ICurrentUser currentUser)
    {
        _audit = audit;
        _currentUser = currentUser;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<AuditEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] AuditQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_currentUser.BusinessId))
        {
            return ApiProblems.SessionInvalid(HttpContext);
        }

        if (query.From is { } from && query.To is { } to && from > to)
        {
            ModelState.AddModelError(nameof(AuditQuery.From), "The start date must be on or before the end date.");
            return ValidationProblem(ModelState);
        }

        return Ok(await _audit.ListAsync(query, cancellationToken));
    }
}
