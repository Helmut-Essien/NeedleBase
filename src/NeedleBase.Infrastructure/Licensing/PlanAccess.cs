using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Licensing;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Infrastructure.Licensing;

/// <summary>Reads the shop's stored licence fields (one primary-key lookup) and applies <see cref="PlanCatalog"/>.</summary>
public sealed class PlanAccess : IPlanAccess
{
    private readonly ApplicationDbContext _db;

    public PlanAccess(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PlanEntitlements?> GetEntitlementsAsync(string businessId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        var licence = await _db.Businesses.AsNoTracking()
            .Where(b => b.Id == businessId)
            .Select(b => new { b.LicenseKeyProtected, b.LicensePlan, b.LicenseIsTrial, b.LicenseIsComplimentary })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return licence is null
            ? null
            : PlanCatalog.Resolve(licence.LicenseKeyProtected, licence.LicensePlan, licence.LicenseIsTrial, licence.LicenseIsComplimentary).Entitlements;
    }
}
