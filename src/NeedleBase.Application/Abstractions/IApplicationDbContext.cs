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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
