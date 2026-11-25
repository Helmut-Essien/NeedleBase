using NeedleBase.Application.Common;
using NeedleBase.Mobile.Data.Services;

namespace NeedleBase.Mobile.Tests;

/// <summary>Stand-in for AuthService tests that never start Free mode.</summary>
internal sealed class NoopFreeModeLocalService : IFreeModeLocalService
{
    public Task<bool> ExistsAsync(string businessId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task CreateAsync(string businessId, string shopName, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<(int CustomersUsed, int OrdersUsed)> GetUsageAsync(
        string businessId, CancellationToken cancellationToken = default) =>
        Task.FromResult((0, 0));

    public Task<RecordFailure?> TryReserveCustomerSlotAsync(
        string businessId, CancellationToken cancellationToken = default) =>
        Task.FromResult<RecordFailure?>(null);

    public Task ReleaseCustomerSlotAsync(string businessId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<RecordFailure?> TryReserveOrderSlotAsync(
        string businessId, CancellationToken cancellationToken = default) =>
        Task.FromResult<RecordFailure?>(null);

    public Task ReleaseOrderSlotAsync(string businessId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
