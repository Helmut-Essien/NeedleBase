using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Api.Http;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Controllers;

/// <summary>
/// Staff directory for display (assignee names on orders and the production board). Active members of the
/// caller's business only; no emails or phone numbers. Invites and role changes are Slice 7.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireStaff")]
[Route("api/staff")]
public sealed class StaffController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public StaffController(ApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
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
}
