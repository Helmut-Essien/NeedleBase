using NeedleBase.Mobile.Data.Services;

namespace NeedleBase.Mobile.Tests;

/// <summary>Stand-in for AuthService tests that never upgrade from Free.</summary>
internal sealed class NoopFreeUpgradeLocalService : IFreeUpgradeLocalService
{
    public Task<bool> HasShopRowsAsync(string businessId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task RemapBusinessIdAsync(
        string fromBusinessId, string toBusinessId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
