using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Abstractions;
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
