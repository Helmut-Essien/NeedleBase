using Microsoft.AspNetCore.Mvc;

namespace NeedleBase.Api.Controllers;

[ApiController]
[Route("api/test")]
public sealed class DiagnosticsController : ControllerBase
{
    /// <summary>Lightweight connectivity ping for mobile / probes.</summary>
    [HttpGet("ping")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Ping() => Ok(new { status = "ok" });
}
