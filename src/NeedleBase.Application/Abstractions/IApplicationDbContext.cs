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

    /// <summary>Sync tombstones (written by the DbContext on delete; read by sync pull).</summary>
    DbSet<DeletedItem> DeletedItems { get; }

    /// <summary>Used by sync push to drop a failed operation's pending changes before the next one.</summary>
    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sync pull upper bound: the oldest transaction still running (PostgreSQL snapshot xmin). Every row
    /// whose <c>SyncTxId</c> is below it is committed (or never will be).
    /// </summary>
    Task<long> GetSyncWatermarkAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction (inside the retrying execution strategy);
    /// commits only when it returns Commit = true, otherwise rolls back.
    /// </summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<(T Result, bool Commit)>> work, CancellationToken cancellationToken = default);
}
