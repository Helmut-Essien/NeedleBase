using Microsoft.EntityFrameworkCore;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Mobile.Data.Data;

/// <summary>
/// Offline SQLite store. Sync queue enqueue lands in Slice 4;
/// <see cref="SuppressSyncQueue"/> is reserved for pull-apply then.
/// </summary>
public sealed class LocalDbContext : DbContext
{
    public LocalDbContext(DbContextOptions<LocalDbContext> options)
        : base(options)
    {
    }

    /// <summary>When true, SaveChanges will not enqueue sync ops (Slice 4).</summary>
    public bool SuppressSyncQueue { get; set; }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<MeasurementProfile> MeasurementProfiles => Set<MeasurementProfile>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

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
    }
}
