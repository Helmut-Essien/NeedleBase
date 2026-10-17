using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Identity;

namespace NeedleBase.Infrastructure.Persistence;

/// <summary>
/// Idempotent sample data for local Development only (Ama Stitches persona from product UX).
/// </summary>
public static class DevelopmentDataSeeder
{
    public const string OwnerEmail = "ama@amastitches.dev";
    public const string TailorEmail = "kofi@amastitches.dev";
    public const string DefaultPassword = "DevPass1!";

    private const string SeedCustomerPhone = "+233245550123";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var config = services.GetRequiredService<IConfiguration>();
        if (!config.GetValue("Seed:DevelopmentData", defaultValue: true))
        {
            return;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DevelopmentDataSeeder");
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<ApplicationDbContext>();
        var clock = services.GetRequiredService<IClock>();

        db.CurrentBusinessId = null;

        var owner = await userManager.FindByEmailAsync(OwnerEmail).ConfigureAwait(false);
        string businessId;

        if (owner is null)
        {
            var now = clock.UtcNow;
            businessId = IdGenerator.NewId();
            var business = Business.Create(businessId, "Ama Stitches", "+233302555010", now);

            owner = new ApplicationUser
            {
                Id = IdGenerator.NewId(),
                UserName = OwnerEmail,
                Email = OwnerEmail,
                EmailConfirmed = true,
                FirstName = "Ama",
                Surname = "Owusu",
                IsActive = true,
                CreatedAtUtc = now
            };

            var createOwner = await userManager.CreateAsync(owner, DefaultPassword).ConfigureAwait(false);
            if (!createOwner.Succeeded)
            {
                throw new InvalidOperationException(
                    "Development seed failed creating owner: " +
                    string.Join("; ", createOwner.Errors.Select(e => e.Description)));
            }

            var tailor = new ApplicationUser
            {
                Id = IdGenerator.NewId(),
                UserName = TailorEmail,
                Email = TailorEmail,
                EmailConfirmed = true,
                FirstName = "Kofi",
                Surname = "Asante",
                IsActive = true,
                CreatedAtUtc = now
            };

            var createTailor = await userManager.CreateAsync(tailor, DefaultPassword).ConfigureAwait(false);
            if (!createTailor.Succeeded)
            {
                throw new InvalidOperationException(
                    "Development seed failed creating tailor: " +
                    string.Join("; ", createTailor.Errors.Select(e => e.Description)));
            }

            db.Businesses.Add(business);
            db.StaffMemberships.Add(StaffMembership.Create(
                IdGenerator.NewId(), businessId, owner.Id, BusinessRole.Owner, now));
            db.StaffMemberships.Add(StaffMembership.Create(
                IdGenerator.NewId(), businessId, tailor.Id, BusinessRole.Tailor, now));

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Seeded development business {BusinessName} ({BusinessId}). Owner: {OwnerEmail}",
                business.Name,
                businessId,
                OwnerEmail);
        }
        else
        {
            var membership = await db.StaffMemberships
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(m => m.UserId == owner.Id && m.IsActive)
                .OrderBy(m => m.JoinedAtUtc)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (membership is null)
            {
                logger.LogWarning(
                    "Seed owner {OwnerEmail} exists without membership; skipping customer seed.",
                    OwnerEmail);
                return;
            }

            businessId = membership.BusinessId;
            await EnsureTailorAsync(userManager, db, clock, businessId, logger, cancellationToken)
                .ConfigureAwait(false);
        }

        await EnsureSampleCustomersAsync(db, clock, businessId, logger, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureTailorAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        IClock clock,
        string businessId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var tailor = await userManager.FindByEmailAsync(TailorEmail).ConfigureAwait(false);
        if (tailor is null)
        {
            var now = clock.UtcNow;
            tailor = new ApplicationUser
            {
                Id = IdGenerator.NewId(),
                UserName = TailorEmail,
                Email = TailorEmail,
                EmailConfirmed = true,
                FirstName = "Kofi",
                Surname = "Asante",
                IsActive = true,
                CreatedAtUtc = now
            };

            var create = await userManager.CreateAsync(tailor, DefaultPassword).ConfigureAwait(false);
            if (!create.Succeeded)
            {
                throw new InvalidOperationException(
                    "Development seed failed creating tailor: " +
                    string.Join("; ", create.Errors.Select(e => e.Description)));
            }

            logger.LogInformation("Seeded development tailor {TailorEmail}", TailorEmail);
        }

        var hasMembership = await db.StaffMemberships
            .IgnoreQueryFilters()
            .AnyAsync(
                m => m.UserId == tailor.Id && m.BusinessId == businessId && m.IsActive,
                cancellationToken)
            .ConfigureAwait(false);

        if (!hasMembership)
        {
            db.StaffMemberships.Add(StaffMembership.Create(
                IdGenerator.NewId(),
                businessId,
                tailor.Id,
                BusinessRole.Tailor,
                clock.UtcNow));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task EnsureSampleCustomersAsync(
        ApplicationDbContext db,
        IClock clock,
        string businessId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var efuaExists = await db.Customers
            .IgnoreQueryFilters()
            .AnyAsync(
                c => c.BusinessId == businessId && c.Phone == SeedCustomerPhone,
                cancellationToken)
            .ConfigureAwait(false);

        if (efuaExists)
        {
            return;
        }

        var now = clock.UtcNow;
        var efua = Customer.Create(
            IdGenerator.NewId(),
            businessId,
            "Efua Mensah",
            SeedCustomerPhone,
            "Prefers Saturday fittings. Accra — East Legon.",
            photoRef: null,
            now);

        var kwame = Customer.Create(
            IdGenerator.NewId(),
            businessId,
            "Kwame Boateng",
            "+233244001122",
            "Regular kaftan client.",
            photoRef: null,
            now);

        var shirtFields = MeasurementFieldsSerializer.Serialize(new Dictionary<string, decimal?>
        {
            ["chest"] = 102m,
            ["shoulder"] = 46m,
            ["sleeve"] = 62m,
            ["shirtLength"] = 74m,
            ["neck"] = 38m
        });

        var kabaFields = MeasurementFieldsSerializer.Serialize(new Dictionary<string, decimal?>
        {
            ["bust"] = 96m,
            ["waist"] = 78m,
            ["hip"] = 104m,
            ["blouseLength"] = 58m,
            ["skirtLength"] = 102m
        });

        var shirt = MeasurementProfile.Create(
            IdGenerator.NewId(),
            businessId,
            efua.Id,
            GarmentTemplate.Shirt,
            MeasurementUnit.Centimetres,
            shirtFields,
            "Client shirt — slightly loose fit.",
            measuredAtUtc: now.AddDays(-14),
            utcNow: now);

        var kaba = MeasurementProfile.Create(
            IdGenerator.NewId(),
            businessId,
            efua.Id,
            GarmentTemplate.SlitAndKaba,
            MeasurementUnit.Centimetres,
            kabaFields,
            notes: null,
            measuredAtUtc: now.AddDays(-3),
            utcNow: now);

        var kaftanFields = MeasurementFieldsSerializer.Serialize(new Dictionary<string, decimal?>
        {
            ["chest"] = 110m,
            ["shoulder"] = 48m,
            ["sleeve"] = 64m,
            ["length"] = 140m
        });

        var kaftan = MeasurementProfile.Create(
            IdGenerator.NewId(),
            businessId,
            kwame.Id,
            GarmentTemplate.Kaftan,
            MeasurementUnit.Centimetres,
            kaftanFields,
            notes: null,
            measuredAtUtc: now.AddDays(-7),
            utcNow: now);

        db.Customers.AddRange(efua, kwame);
        db.MeasurementProfiles.AddRange(shirt, kaba, kaftan);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Seeded development customers for business {BusinessId} (Efua Mensah, Kwame Boateng).",
            businessId);
    }
}
