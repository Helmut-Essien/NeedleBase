using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Api.Http;
using NeedleBase.Api.Updates;

namespace NeedleBase.Api.Controllers;

/// <summary>In-app update check for the sideloaded Android app (manifest from the newest GitHub Release).</summary>
[ApiController]
[Route("api/mobile/update")]
[AllowAnonymous]
[AllowWhenReadOnly]
public sealed class MobileUpdateController(MobileUpdateFeed feed) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-cache";
        var json = await feed.GetManifestJsonAsync(ct);
        return json is null
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "No update information available right now." })
            : Content(json, "application/json");
    }
}
