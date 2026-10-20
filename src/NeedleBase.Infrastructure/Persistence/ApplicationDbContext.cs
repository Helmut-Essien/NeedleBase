using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Sync;
using NeedleBase.Domain.Enums;
using NeedleBase.Domain.Entities;
using NeedleBase.Infrastructure.Identity;

namespace NeedleBase.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL store. Tenant isolation: set <see cref="CurrentBusinessId"/> from JWT claims
/// so global query filters apply to <see cref="IBusinessScoped"/> entities.
/// Documented approach for Slice 1+: <b>global query filters</b> (not per-query only).
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// All <see cref="IBusinessScoped"/> queries are filtered to this business.
    /// Fail-closed: when null, filtered queries return no rows. Auth/bootstrap paths that
    /// legitimately read across businesses must use <c>IgnoreQueryFilters()</c> scoped by user id.
    /// </summary>
    public string? CurrentBusinessId { get; set; }

    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<StaffMembership> StaffMemberships => Set<StaffMembership>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<MeasurementProfile> MeasurementProfiles => Set<MeasurementProfile>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<DeletedItem> DeletedItems => Set<DeletedItem>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampSyncChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await StampSyncChangesAsync(cancellationToken).ConfigureAwait(false);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> GetSyncWatermarkAsync(CancellationToken cancellationToken = default) =>
        await Database.SqlQueryRaw<long>("SELECT pg_snapshot_xmin(pg_current_snapshot())::text::bigint AS \"Value\"")
            .SingleAsync(cancellationToken).ConfigureAwait(false);

    public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<(T Result, bool Commit)>> work, CancellationToken cancellationToken = default)
    {
        var strategy = Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async ct =>
        {
            ChangeTracker.Clear();
            await using var transaction = await Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var (result, commit) = await work(ct).ConfigureAwait(false);
            if (commit)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
            else
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                ChangeTracker.Clear();
            }

            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// Sync bookkeeping for every writer (REST, sync push, seeding): stamp <see cref="SyncTime.ModifiedColumn"/>
    /// on added/changed synced rows, and write a <see cref="DeletedItem"/> tombstone for each deleted
    /// synced row, including rows the database cascades (a customer's measurements, an order's lines).
    /// </summary>
    private async Task StampSyncChangesAsync(CancellationToken cancellationToken)
    {
        var now = SyncTime.Normalize(DateTimeOffset.UtcNow);
        var tombstones = new List<(string BusinessId, string Type, string Id)>();
        var recreated = new List<(string BusinessId, string Type, string Id)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            var type = entry.Entity switch
            {
                Customer => SyncEntityType.Customer,
                MeasurementProfile => SyncEntityType.Measurement,
                Order => SyncEntityType.Order,
                OrderLine => SyncEntityType.OrderLine,
                _ => null
            };
            if (type is null)
            {
                continue;
            }

            if (entry.State is EntityState.Added or EntityState.Modified && type != SyncEntityType.OrderLine)
            {
                entry.Property(SyncTime.ModifiedColumn).CurrentValue = now;
                if (entry.State == EntityState.Added)
                {
                    recreated.Add((((IBusinessScoped)entry.Entity).BusinessId, type, ((Entity)entry.Entity).Id));
                }
            }
            else if (entry.State == EntityState.Deleted)
            {
                var scoped = (IBusinessScoped)entry.Entity;
                var id = ((Entity)entry.Entity).Id;
                if (seen.Add(type + ":" + id))
                {
                    tombstones.Add((scoped.BusinessId, type, id));
                }

                if (entry.Entity is Customer customer)
                {
                    var measurementIds = await MeasurementProfiles.IgnoreQueryFilters().AsNoTracking()
                        .Where(m => m.BusinessId == customer.BusinessId && m.CustomerId == customer.Id)
                        .Select(m => m.Id)
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    foreach (var mid in measurementIds.Where(mid => seen.Add(SyncEntityType.Measurement + ":" + mid)))
                    {
                        tombstones.Add((customer.BusinessId, SyncEntityType.Measurement, mid));
                    }
                }
                else if (entry.Entity is Order order)
                {
                    var lineIds = await OrderLines.IgnoreQueryFilters().AsNoTracking()
                        .Where(l => l.BusinessId == order.BusinessId && l.OrderId == order.Id)
                        .Select(l => l.Id)
                        .ToListAsync(cancellationToken).ConfigureAwait(false);
                    foreach (var lid in lineIds.Where(lid => seen.Add(SyncEntityType.OrderLine + ":" + lid)))
                    {
                        tombstones.Add((order.BusinessId, SyncEntityType.OrderLine, lid));
                    }
                }
            }
        }

        // A record re-created with the id of a deleted one (sync "Keep mine" after a delete elsewhere):
        // its old tombstone must go, in the same transaction, or devices would delete it again on pull.
        if (recreated.Count > 0)
        {
            var ids = recreated.Select(r => r.Id).Distinct().ToList();
            var stale = await DeletedItems.IgnoreQueryFilters()
                .Where(d => ids.Contains(d.EntityId))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            DeletedItems.RemoveRange(stale.Where(d => recreated.Contains((d.BusinessId, d.EntityType, d.EntityId))));
        }

        foreach (var (businessId, type, id) in tombstones)
        {
            DeletedItems.Add(DeletedItem.Create(IdGenerator.NewId(), businessId, type, id, now));
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Business>(e =>
        {
            e.ToTable("Businesses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(26);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            e.Property(x => x.MeasurementUnit).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Name);
        });

        builder.Entity<StaffMembership>(e =>
        {
            e.ToTable("StaffMemberships");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(26);
            e.Property(x => x.BusinessId).HasMaxLength(26).IsRequired();
            e.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.Role).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.BusinessId);
            e.HasIndex(x => new { x.BusinessId, x.UserId }).IsUnique();
            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => CurrentBusinessId != null && x.BusinessId == CurrentBusinessId);
        });

        builder.Entity<Customer>(e =>
        {
            e.ToTable("Customers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(Domain.Common.FieldLimits.Id);
            e.Property(x => x.BusinessId).HasMaxLength(Domain.Common.FieldLimits.Id).IsRequired();
            e.Property(x => x.Name).HasMaxLength(Domain.Common.FieldLimits.CustomerName).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(Domain.Common.FieldLimits.Phone).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(Domain.Common.FieldLimits.Notes);
            e.Property(x => x.PhotoRef).HasMaxLength(Domain.Common.FieldLimits.PhotoRef);
            e.HasIndex(x => x.BusinessId);
            e.HasIndex(x => new { x.BusinessId, x.Name });
            e.HasIndex(x => new { x.BusinessId, x.Phone });
            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => CurrentBusinessId != null && x.BusinessId == CurrentBusinessId);
        });

        builder.Entity<MeasurementProfile>(e =>
        {
            e.ToTable("MeasurementProfiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(Domain.Common.FieldLimits.Id);
            e.Property(x => x.BusinessId).HasMaxLength(Domain.Common.FieldLimits.Id).IsRequired();
            e.Property(x => x.CustomerId).HasMaxLength(Domain.Common.FieldLimits.Id).IsRequired();
            e.Property(x => x.GarmentTemplate).HasMaxLength(Domain.Common.FieldLimits.GarmentTemplate).IsRequired();
            e.Property(x => x.Unit).HasMaxLength(Domain.Common.FieldLimits.MeasurementUnit).IsRequired();
            e.Property(x => x.FieldsJson).HasMaxLength(Domain.Common.FieldLimits.FieldsJson).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(Domain.Common.FieldLimits.Notes);
            e.HasIndex(x => x.BusinessId);
            e.HasIndex(x => new { x.BusinessId, x.CustomerId, x.MeasuredAtUtc });
            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => CurrentBusinessId != null && x.BusinessId == CurrentBusinessId);
        });

        builder.Entity<Order>(e =>
        {
            e.ToTable("Orders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(Domain.Common.FieldLimits.Id);
            e.Property(x => x.BusinessId).HasMaxLength(Domain.Common.FieldLimits.Id).IsRequired();
            e.Property(x => x.CustomerId).HasMaxLength(Domain.Common.FieldLimits.Id).IsRequired();
            e.Property(x => x.DisplayRef).HasMaxLength(Domain.Common.FieldLimits.OrderDisplayRef).IsRequired();
            e.Property(x => x.Status).HasMaxLength(Domain.Common.FieldLimits.OrderStatus).IsRequired();
            e.Property(x => x.AssignedToUserId).HasMaxLength(Domain.Common.FieldLimits.AssignedToUserId);
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.Notes).HasMaxLength(Domain.Common.FieldLimits.Notes);
            e.HasIndex(x => x.BusinessId);
            e.HasIndex(x => new { x.BusinessId, x.Status });
            e.HasIndex(x => new { x.BusinessId, x.DueDate });
            e.HasIndex(x => new { x.BusinessId, x.DisplayRef }).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.CustomerId });
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => CurrentBusinessId != null && x.BusinessId == CurrentBusinessId);
        });

        builder.Entity<OrderLine>(e =>
        {
            e.ToTable("OrderLines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(Domain.Common.FieldLimits.Id);
            e.Property(x => x.BusinessId).HasMaxLength(Domain.Common.FieldLimits.Id).IsRequired();
            e.Property(x => x.OrderId).HasMaxLength(Domain.Common.FieldLimits.Id).IsRequired();
            e.Property(x => x.GarmentTemplate).HasMaxLength(Domain.Common.FieldLimits.GarmentTemplate).IsRequired();
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.MeasurementProfileId).HasMaxLength(Domain.Common.FieldLimits.Id);
            e.Property(x => x.Unit).HasMaxLength(Domain.Common.FieldLimits.MeasurementUnit).IsRequired();
            e.Property(x => x.MeasurementFieldsJson).HasMaxLength(Domain.Common.FieldLimits.FieldsJson).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(Domain.Common.FieldLimits.Notes);
            e.HasIndex(x => x.BusinessId);
            e.HasIndex(x => new { x.BusinessId, x.OrderId });
            // Restrict: Business→Order→OrderLine already cascades; avoid multiple cascade paths.
            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => CurrentBusinessId != null && x.BusinessId == CurrentBusinessId);
        });

        // Sync change stamp (shadow; server clock, µs precision). Existing rows get now() on migration.
        foreach (var synced in new[] { typeof(Customer), typeof(MeasurementProfile), typeof(Order) })
        {
            builder.Entity(synced).Property<DateTimeOffset>(SyncTime.ModifiedColumn).HasDefaultValueSql("now()");
            builder.Entity(synced).HasIndex("BusinessId", SyncTime.ModifiedColumn, "Id");
            // Commit-ordered pull key, written only by the database trigger (see migration SyncTxOrder).
            builder.Entity(synced).Property<long>(SyncTime.TxColumn).ValueGeneratedOnAddOrUpdate();
            builder.Entity(synced).HasIndex("BusinessId", SyncTime.TxColumn, "Id");
        }

        builder.Entity<DeletedItem>(e =>
        {
            e.ToTable("DeletedItems");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(Domain.Common.FieldLimits.Id);
            e.Property(x => x.BusinessId).HasMaxLength(Domain.Common.FieldLimits.Id).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(Domain.Common.FieldLimits.SyncEntityType).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(Domain.Common.FieldLimits.Id).IsRequired();
            e.HasIndex(x => x.BusinessId);
            e.HasIndex(x => new { x.BusinessId, x.DeletedAtUtc, x.Id });
            e.HasIndex(x => new { x.BusinessId, x.EntityType, x.EntityId });
            e.HasIndex(x => x.DeletedAtUtc);
            e.Property<long>(SyncTime.TxColumn).ValueGeneratedOnAddOrUpdate(); // set by the insert trigger
            e.HasIndex("BusinessId", SyncTime.TxColumn, "Id");
            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => CurrentBusinessId != null && x.BusinessId == CurrentBusinessId);
        });

        builder.Entity<RefreshToken>(e =>
        {
            e.ToTable("RefreshTokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(26);
            e.Property(x => x.Token).HasMaxLength(256).IsRequired();
            e.HasIndex(x => x.Token).IsUnique();
            e.HasOne(x => x.ApplicationUser)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(x => x.ApplicationUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Surname).HasMaxLength(100).IsRequired();
        });
    }
}
