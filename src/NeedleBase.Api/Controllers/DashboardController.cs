using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Contracts.Dashboard;
using NeedleBase.Application.Dashboard;

namespace NeedleBase.Api.Controllers;

/// <summary>Web dashboard numbers. Money totals are null for tailors.</summary>
[ApiController]
[Authorize(Policy = "RequireStaff")]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard)
    {
        _dashboard = dashboard;
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(DashboardSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardSummaryDto>> Summary(CancellationToken cancellationToken) =>
        Ok(await _dashboard.GetSummaryAsync(cancellationToken));
}
