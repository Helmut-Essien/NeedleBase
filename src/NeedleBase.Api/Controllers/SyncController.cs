using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Api.Http;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Contracts.Sync;

namespace NeedleBase.Api.Controllers;

/// <summary>
/// Offline sync (Slice 4). Business-scoped by the JWT business claim (fail-closed query filters).
/// Push returns a result per operation (applied / conflict / failed); pull pages changes by cursor.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireStaff")]
[Route("api/sync")]
public sealed class SyncController : ControllerBase
{
    private readonly ISyncService _sync;

    public SyncController(ISyncService sync)
    {
        _sync = sync;
    }

    [HttpPost("push")]
    [ProducesResponseType(typeof(SyncPushResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SyncPushResponse>> Push(SyncPushRequest request, CancellationToken cancellationToken) =>
        Ok(await _sync.PushAsync(request, cancellationToken));

    [HttpPost("pull")]
    [ProducesResponseType(typeof(SyncPullResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SyncPullResponse>> Pull(SyncPullRequest request, CancellationToken cancellationToken)
    {
        var result = await _sync.PullAsync(request, cancellationToken);
        return result is null ? ApiProblems.SyncCursorInvalid(HttpContext) : Ok(result);
    }
}
