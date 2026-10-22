using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Api.Http;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Application.Audit;

namespace NeedleBase.Api.Controllers;

/// <summary>
/// Offline sync (Slice 4). Business-scoped by the JWT business claim (fail-closed query filters).
/// Push returns a result per operation (applied / conflict / failed); pull pages changes by cursor.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireStaff")]
[Route("api/sync")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(SyncRateLimit.PolicyName)]
public sealed class SyncController : ControllerBase
{
    private readonly ISyncService _sync;
    private readonly AuditContext _auditContext;
    private readonly IAuditLog _audit;
    private readonly ICurrentUser _currentUser;

    public SyncController(ISyncService sync, AuditContext auditContext, IAuditLog audit, ICurrentUser currentUser)
    {
        _sync = sync;
        _auditContext = auditContext;
        _audit = audit;
        _currentUser = currentUser;
    }

    /// <summary>A quiet phone writes at most one sync row per hour; pushes with conflicts/failures always get one.</summary>
    public static readonly TimeSpan SyncAuditWindow = TimeSpan.FromHours(1);

    private string DeviceKey(string? deviceId) => $"{_currentUser.BusinessId}:{_currentUser.UserId}:{deviceId ?? "unknown"}";

    [HttpPost("push")]
    [ProducesResponseType(typeof(SyncPushResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SyncPushResponse>> Push(SyncPushRequest request, CancellationToken cancellationToken)
    {
        // Writes would queue behind the re-stamp's row locks; let the device retry instead.
        if (NeedleBase.Infrastructure.Persistence.ApplicationDbContext.EpochBumpRunning)
        {
            return ApiProblems.SyncMaintenance(HttpContext);
        }

        if (!string.IsNullOrWhiteSpace(request.DeviceId))
        {
            _auditContext.DeviceId = request.DeviceId.Trim(); // per-change audit rows carry the phone's id
        }

        var response = await _sync.PushAsync(request, cancellationToken);
        var applied = response.Results.Count(r => r.Status == SyncOpStatus.Applied);
        var conflicts = response.Results.Count(r => r.Status == SyncOpStatus.Conflict);
        var failed = response.Results.Count - applied - conflicts;
        var summary = $"Phone sent {response.Results.Count} change{(response.Results.Count == 1 ? "" : "s")}: {applied} saved"
                      + (conflicts > 0 ? $", {conflicts} need a choice on the phone" : "")
                      + (failed > 0 ? $", {failed} refused" : "");
        if (response.Results.Count > 0) // an empty push changed nothing: no row
        {
            await _audit.WriteAsync(new AuditEntry(AuditActions.SyncPush, summary),
                throttleKey: conflicts + failed > 0 ? null : "sync.push:" + DeviceKey(_auditContext.DeviceId), window: SyncAuditWindow,
                cancellationToken: cancellationToken);
        }
        return Ok(response);
    }

    [HttpPost("pull")]
    [ProducesResponseType(typeof(SyncPullResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SyncPullResponse>> Pull(SyncPullRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(request.DeviceId))
            {
                _auditContext.DeviceId = request.DeviceId.Trim();
            }

            var (page, error) = await _sync.PullAsync(request, cancellationToken);
            if (page is not null && !page.HasMore)
            {
                // End of a pull session (not every page): at most one row per phone per hour.
                await _audit.WriteAsync(new AuditEntry(AuditActions.SyncPull, "Phone synced"),
                    throttleKey: "sync.pull:" + DeviceKey(_auditContext.DeviceId), window: SyncAuditWindow, cancellationToken: cancellationToken);
            }

            return page is null ? ApiProblems.SyncCursorInvalid(HttpContext, error) : Ok(page);
        }
        catch (SyncMaintenanceException)
        {
            return ApiProblems.SyncMaintenance(HttpContext);
        }
    }
}
