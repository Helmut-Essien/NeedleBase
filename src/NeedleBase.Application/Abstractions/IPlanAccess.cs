using NeedleBase.Application.Licensing;

namespace NeedleBase.Application.Abstractions;

/// <summary>What the shop's current plan allows (<see cref="PlanCatalog"/> applied to the stored licence fields).</summary>
public interface IPlanAccess
{
    /// <summary>The shop's entitlements, or null when the shop doesn't exist.</summary>
    Task<PlanEntitlements?> GetEntitlementsAsync(string businessId, CancellationToken cancellationToken = default);
}
