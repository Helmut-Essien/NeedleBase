using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Common;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Mobile.Data.Data;

/// <summary>
/// Offline SQLite store. SaveChanges enqueues sync items (Slice 4) unless SuppressSyncQueue;
/// pull-apply sets <see cref="SuppressSyncQueue"/> (reset in finally).
/// </summary>
public sealed class LocalDbContext : DbContext
{
    public LocalDbContext(DbContextOptions<LocalDbContext> options)
        : base(options)
    {
    }

    /// <summary>When true, SaveChanges will not enqueue sync ops (Slice 4).</summary>
    public bool SuppressSyncQueue { get; set; }

    public const string SearchNameColumn = CustomerSearchKeys.SearchNameColumn;
    public const string PhoneDigitsColumn = CustomerSearchKeys.PhoneDigitsColumn;

    /// <summary>
    /// Shadow flag (schema v6): true once the server is known to have the row (pulled, or a push was applied).
    /// A row that is neither server-known nor queued was made by a build that had no sync queue; the engine
    /// adopts it (queues it as a create) instead of pruning it.
    /// </summary>
    public const string ServerKnownColumn = "ServerKnown";

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampCustomerSearchKeys();
        var queued = !SuppressSyncQueue && SyncQueueWriter.EnqueueAsync(this, CancellationToken.None).GetAwaiter().GetResult();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        if (queued)
        {
            Sync.LocalSyncSignals.RaiseQueueChanged();
        }

        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampCustomerSearchKeys();
        var queued = !SuppressSyncQueue && await SyncQueueWriter.EnqueueAsync(this, cancellationToken).ConfigureAwait(false);
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        if (queued)
        {
            Sync.LocalSyncSignals.RaiseQueueChanged();
        }

        return result;
    }

    /// <summary>Every writer (forms now, sync pull in Slice 4) gets the same search keys.</summary>
    private void StampCustomerSearchKeys()
    {
        foreach (var entry in ChangeTracker.Entries<Customer>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            entry.Property(SearchNameColumn).CurrentValue = CustomerSearchKeys.NameKey(entry.Entity.Name);
            entry.Property(PhoneDigitsColumn).CurrentValue = CustomerSearchKeys.PhoneDigits(entry.Entity.Phone);
        }
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<MeasurementProfile> MeasurementProfiles => Set<MeasurementProfile>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<SyncQueueItem> SyncQueue => Set<SyncQueueItem>();
    public DbSet<DeviceInfo> DeviceInfo => Set<DeviceInfo>();
    public DbSet<SyncDeferredItem> SyncDeferred => Set<SyncDeferredItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Customer>(e =>
        {
            e.ToTable("Customers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(FieldLimits.Id);
            e.Property(x => x.BusinessId).HasMaxLength(FieldLimits.Id).IsRequired();
            e.Property(x => x.Name).HasMaxLength(FieldLimits.CustomerName).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(FieldLimits.Phone).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(FieldLimits.Notes);
            e.Property(x => x.PhotoRef).HasMaxLength(FieldLimits.PhotoRef);
            e.HasIndex(x => x.BusinessId);
            e.HasIndex(x => new { x.BusinessId, x.Name });

            // Local-only search keys (schema v3), kept in sync by SaveChanges. See CustomerSearchKeys.
            e.Property<string>(SearchNameColumn).HasMaxLength(FieldLimits.CustomerName + 1);
            e.Property<string>(PhoneDigitsColumn).HasMaxLength(FieldLimits.Phone);
            e.HasIndex(nameof(Customer.BusinessId), SearchNameColumn);
            e.HasIndex(nameof(Customer.BusinessId), PhoneDigitsColumn);
            e.Property<bool>(ServerKnownColumn);
            e.HasIndex(x => x.BusinessId, "IX_Customers_Unknown").HasFilter("\"ServerKnown\" = 0");
        });

        builder.Entity<MeasurementProfile>(e =>
        {
            e.ToTable("MeasurementProfiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(FieldLimits.Id);
            e.Property(x => x.BusinessId).HasMaxLength(FieldLimits.Id).IsRequired();
            e.Property(x => x.CustomerId).HasMaxLength(FieldLimits.Id).IsRequired();
            e.Property(x => x.GarmentTemplate).HasMaxLength(FieldLimits.GarmentTemplate).IsRequired();
            e.Property(x => x.Unit).HasMaxLength(FieldLimits.MeasurementUnit).IsRequired();
            e.Property(x => x.FieldsJson).HasMaxLength(FieldLimits.FieldsJson).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(FieldLimits.Notes);
            e.HasIndex(x => x.BusinessId);
            e.HasIndex(x => new { x.BusinessId, x.CustomerId, x.MeasuredAtUtc });
            e.Property<bool>(ServerKnownColumn);
            e.HasIndex(x => x.BusinessId, "IX_MeasurementProfiles_Unknown").HasFilter("\"ServerKnown\" = 0");
            e.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Order>(e =>
        {
            e.ToTable("Orders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(FieldLimits.Id);
            e.Property(x => x.BusinessId).HasMaxLength(FieldLimits.Id).IsRequired();
            e.Property(x => x.CustomerId).HasMaxLength(FieldLimits.Id).IsRequired();
            e.Property(x => x.DisplayRef).HasMaxLength(FieldLimits.OrderDisplayRef).IsRequired();
            e.Property(x => x.Status).HasMaxLength(FieldLimits.OrderStatus).IsRequired();
            e.Property(x => x.AssignedToUserId).HasMaxLength(FieldLimits.AssignedToUserId);
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.Notes).HasMaxLength(FieldLimits.Notes);
            e.HasIndex(x => x.BusinessId);
            e.HasIndex(x => new { x.BusinessId, x.Status });
            e.HasIndex(x => new { x.BusinessId, x.DueDate });
            e.HasIndex(x => new { x.BusinessId, x.DisplayRef }).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.CustomerId });
            e.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            e.Property<bool>(ServerKnownColumn);
            e.HasIndex(x => x.BusinessId, "IX_Orders_Unknown").HasFilter("\"ServerKnown\" = 0");
            e.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OrderLine>(e =>
        {
            e.ToTable("OrderLines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(FieldLimits.Id);
            e.Property(x => x.BusinessId).HasMaxLength(FieldLimits.Id).IsRequired();
            e.Property(x => x.OrderId).HasMaxLength(FieldLimits.Id).IsRequired();
            e.Property(x => x.GarmentTemplate).HasMaxLength(FieldLimits.GarmentTemplate).IsRequired();
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.MeasurementProfileId).HasMaxLength(FieldLimits.Id);
            e.Property(x => x.Unit).HasMaxLength(FieldLimits.MeasurementUnit).IsRequired();
            e.Property(x => x.MeasurementFieldsJson).HasMaxLength(FieldLimits.FieldsJson).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(FieldLimits.Notes);
            e.HasIndex(x => x.BusinessId);
            e.HasIndex(x => new { x.BusinessId, x.OrderId });
        });

        builder.Entity<SyncQueueItem>(e =>
        {
            e.ToTable("SyncQueue");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(FieldLimits.Id);
            e.Property(x => x.BusinessId).HasMaxLength(FieldLimits.Id).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(FieldLimits.SyncEntityType).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(FieldLimits.Id).IsRequired();
            e.Property(x => x.ParentId).HasMaxLength(FieldLimits.Id);
            e.Property(x => x.Operation).HasMaxLength(FieldLimits.SyncOperation).IsRequired();
            e.Property(x => x.Label).HasMaxLength(FieldLimits.CustomerName + 20).IsRequired();
            e.Property(x => x.State).HasMaxLength(FieldLimits.SyncOperation).IsRequired();
            e.HasIndex(x => new { x.BusinessId, x.EntityType, x.EntityId }).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.State });
        });

        builder.Entity<SyncDeferredItem>(e =>
        {
            e.ToTable("SyncDeferred");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(FieldLimits.Id);
            e.Property(x => x.BusinessId).HasMaxLength(FieldLimits.Id).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(FieldLimits.SyncEntityType).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(FieldLimits.Id).IsRequired();
            e.Property(x => x.ParentId).HasMaxLength(FieldLimits.Id).IsRequired();
            e.Property(x => x.PayloadJson).IsRequired();
            e.HasIndex(x => new { x.BusinessId, x.EntityType, x.EntityId }).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.ParentId });
        });

        builder.Entity<DeviceInfo>(e =>
        {
            e.ToTable("DeviceInfo");
            e.HasKey(x => x.BusinessId);
            e.Property(x => x.BusinessId).HasMaxLength(FieldLimits.Id);
            e.Property(x => x.DeviceId).HasMaxLength(FieldLimits.Id).IsRequired();
            e.Property(x => x.PullCursor).HasMaxLength(2000);
        });
    }
}
