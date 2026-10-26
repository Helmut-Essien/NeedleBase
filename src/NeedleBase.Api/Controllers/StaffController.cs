using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Api.Http;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Controllers;

/// <summary>
/// Staff directory (names for assignees, any member) and the Staff screen: Owner/Manager can view; only the Owner
/// invites, changes roles and turns access off/on.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireStaff")]
[Route("api/staff")]
public sealed class StaffController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IStaffService _staff;

    public StaffController(ApplicationDbContext db, ICurrentUser currentUser, IStaffService staff)
    {
        _db = db;
        _currentUser = currentUser;
        _staff = staff;
    }

    [HttpGet("names")]
    [ProducesResponseType(typeof(IReadOnlyList<StaffNameDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Names(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_currentUser.BusinessId))
        {
            return ApiProblems.SessionInvalid(HttpContext);
        }

        // StaffMemberships is tenant-filtered: only this business's members.
        var rows = await (
                from m in _db.StaffMemberships.AsNoTracking().Where(m => m.IsActive)
                join u in _db.Users.AsNoTracking() on m.UserId equals u.Id
                orderby u.FirstName, u.Surname
                select new { m.UserId, u.FirstName, u.Surname, m.Role })
            .Take(500)
            .ToListAsync(cancellationToken);
        return Ok(rows.Select(r => new StaffNameDto(r.UserId, StaffNameDto.Compose(r.FirstName, r.Surname), r.Role)).ToList());
    }

    /// <summary>Members (with open assigned work) and recent invites.</summary>
    [HttpGet]
    [Authorize(Policy = "RequireManagerOrOwner")]
    [ProducesResponseType(typeof(StaffOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(_currentUser.BusinessId)
            ? ApiProblems.SessionInvalid(HttpContext)
            : Ok(await _staff.GetOverviewAsync(cancellationToken));

    /// <summary>Creates an invite; the response carries the one-time code to share (it is not stored).</summary>
    [HttpPost("invitations")]
    [Authorize(Policy = "RequireOwner")]
    [ProducesResponseType(typeof(CreatedInvitationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Invite([FromBody] CreateInvitationRequest request, CancellationToken cancellationToken)
    {
        var result = await _staff.InviteAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ApiProblems.ForStaff(HttpContext, result.ErrorCode);
    }

    [HttpDelete("invitations/{id}")]
    [Authorize(Policy = "RequireOwner")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(string id, CancellationToken cancellationToken)
    {
        var result = await _staff.RevokeInvitationAsync(id, cancellationToken);
        return result.Succeeded ? NoContent() : ApiProblems.ForStaff(HttpContext, result.ErrorCode);
    }

    [HttpPut("{userId}/role")]
    [Authorize(Policy = "RequireOwner")]
    [ProducesResponseType(typeof(StaffMemberDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeRole(string userId, [FromBody] ChangeStaffRoleRequest request, CancellationToken cancellationToken)
    {
        var result = await _staff.ChangeRoleAsync(userId, request.Role, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ApiProblems.ForStaff(HttpContext, result.ErrorCode);
    }

    [HttpPost("{userId}/deactivate")]
    [Authorize(Policy = "RequireOwner")]
    [ProducesResponseType(typeof(StaffMemberDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Deactivate(string userId, CancellationToken cancellationToken) => SetActive(userId, false, cancellationToken);

    [HttpPost("{userId}/reactivate")]
    [Authorize(Policy = "RequireOwner")]
    [ProducesResponseType(typeof(StaffMemberDto), StatusCodes.Status200OK)]
    public Task<IActionResult> Reactivate(string userId, CancellationToken cancellationToken) => SetActive(userId, true, cancellationToken);

    /// <summary>Owner: move this member's open orders to another active member, or unassign them (no toUserId).</summary>
    [HttpPost("{userId}/reassign-orders")]
    [Authorize(Policy = "RequireOwner")]
    [ProducesResponseType(typeof(ReassignOrdersResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ReassignOrders(string userId, [FromBody] ReassignOrdersRequest? request, CancellationToken cancellationToken)
    {
        var result = await _staff.ReassignOpenOrdersAsync(userId, request?.ToUserId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ApiProblems.ForStaff(HttpContext, result.ErrorCode);
    }

    private async Task<IActionResult> SetActive(string userId, bool active, CancellationToken cancellationToken)
    {
        var result = await _staff.SetActiveAsync(userId, active, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : ApiProblems.ForStaff(HttpContext, result.ErrorCode);
    }
}
