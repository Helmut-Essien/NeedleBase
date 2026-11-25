using NeedleBase.Application.Common;

namespace NeedleBase.Mobile.Data.Services;

/// <summary>Lifetime counters and setup for Free (phone only) on this device.</summary>
public interface IFreeModeLocalService
{
    Task<bool> ExistsAsync(string businessId, CancellationToken cancellationToken = default);

    Task CreateAsync(string businessId, string shopName, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<(int CustomersUsed, int OrdersUsed)> GetUsageAsync(string businessId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically reserves one Free customer slot when this business is Free.
    /// Null = reserved (or not Free). Failure = at cap or Free state missing (fail closed).
    /// </summary>
    Task<RecordFailure?> TryReserveCustomerSlotAsync(string businessId, CancellationToken cancellationToken = default);

    /// <summary>Undoes a reserve when the customer row could not be saved.</summary>
    Task ReleaseCustomerSlotAsync(string businessId, CancellationToken cancellationToken = default);

    Task<RecordFailure?> TryReserveOrderSlotAsync(string businessId, CancellationToken cancellationToken = default);

    Task ReleaseOrderSlotAsync(string businessId, CancellationToken cancellationToken = default);
}
