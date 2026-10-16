using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Domain.Entities;
using NeedleBase.Infrastructure.Identity;

namespace NeedleBase.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL store. Tenant isolation: set <see cref="CurrentBusinessId"/> from JWT claims
/// so global query filters apply to <see cref="IBusinessScoped"/> entities.
/// Documented approach for Slice 1+: <b>global query filters</b> (not per-query only).
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// When non-null, all <see cref="IBusinessScoped"/> queries are filtered to this business.
    /// Leave null for auth/bootstrap paths that must read across memberships.
    /// </summary>
    public string? CurrentBusinessId { get; set; }

    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<StaffMembership> StaffMemberships => Set<StaffMembership>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

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
            e.HasQueryFilter(x => CurrentBusinessId == null || x.BusinessId == CurrentBusinessId);
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
