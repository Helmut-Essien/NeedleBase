using Microsoft.EntityFrameworkCore;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Application.Abstractions;

/// <summary>
/// Persistence port for use-case services. Implemented by Infrastructure's
/// ApplicationDbContext; business-scoped sets are tenant-filtered (fail-closed).
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Customer> Customers { get; }

    DbSet<MeasurementProfile> MeasurementProfiles { get; }

    /// <summary>Read-only use: assignee membership checks (tenant-filtered).</summary>
    DbSet<StaffMembership> StaffMemberships { get; }

    DbSet<Order> Orders { get; }

    DbSet<OrderLine> OrderLines { get; }

    DbSet<Payment> Payments { get; }

    /// <summary>Sync tombstones (written by the DbContext on delete; read by sync pull).</summary>
    DbSet<DeletedItem> DeletedItems { get; }

    /// <summary>Used by sync push to drop a failed operation's pending changes before the next one.</summary>
    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sync pull upper bound: the oldest transaction still running (PostgreSQL snapshot xmin). Every row
    /// whose <c>SyncTxId</c> is below it is committed (or never will be).
    /// Also returns the sync epoch; if the database moved to a new cluster (watermark went backwards) the epoch
    /// is bumped first and every synced row is re-stamped.
    /// </summary>
    /// <param name="minimumCap">
    /// Highest transaction position a client cursor already holds. A cap below it means transaction ids went
    /// backwards (restore), even if the stored high-water mark was restored too, so the epoch is bumped.
    /// </param>
    Task<SyncWatermark> GetSyncWatermarkAsync(long minimumCap = 0, CancellationToken cancellationToken = default);

    /// <summary>Current sync epoch (see <see cref="GetSyncWatermarkAsync"/>).</summary>
    Task<long> GetSyncEpochAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction (inside the retrying execution strategy);
    /// commits only when it returns Commit = true, otherwise rolls back.
    /// </summary>
    /// <summary>True while a transaction from <see cref="InTransactionAsync{T}"/> (or sync push) is open.</summary>
    bool HasActiveTransaction { get; }

    /// <summary>
    /// Inside a transaction: serialises payment writes on one order (row lock) and on one payment id
    /// (transaction advisory lock), so balance checks and id-replay checks can't race.
    /// </summary>
    /// <summary>Inside a transaction: row-locks one order (delete vs. a payment being recorded on it).</summary>
    Task LockOrderAsync(string orderId, CancellationToken cancellationToken = default);

    Task LockForPaymentAsync(string orderId, string paymentId, CancellationToken cancellationToken = default);

    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<(T Result, bool Commit)>> work, CancellationToken cancellationToken = default);
}

/// <summary>Pull upper bound (snapshot xmin) and the sync epoch it belongs to.</summary>
public sealed record SyncWatermark(long Cap, long Epoch);
