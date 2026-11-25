namespace NeedleBase.Mobile.Data.Services;

/// <summary>Moves Free (phone-only) local rows onto a new online BusinessId for first sync.</summary>
public interface IFreeUpgradeLocalService
{
    /// <summary>True when any customer/order/measurement/payment still uses this business id.</summary>
    Task<bool> HasShopRowsAsync(string businessId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewrites BusinessId on all local shop tables from Free → online, drops FreeModeStates for
    /// <paramref name="fromBusinessId"/>. Safe to call again if already remapped (no-op when no from-rows).
    /// </summary>
    Task RemapBusinessIdAsync(string fromBusinessId, string toBusinessId, CancellationToken cancellationToken = default);
}
