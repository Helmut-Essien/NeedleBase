using NeedleBase.Api.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Controllers;

/// <summary>Protected probe: requires auth + business_id claim (Slice 1 acceptance).</summary>
[ApiController]
[Route("api/me")]
[Authorize(Policy = "RequireStaff")]
public sealed class MeController : ControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly ApplicationDbContext _db;

    public MeController(ICurrentUser currentUser, ApplicationDbContext db)
    {
        _currentUser = currentUser;
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
}
