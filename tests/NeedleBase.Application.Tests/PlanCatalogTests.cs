using NeedleBase.Application.Licensing;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Application.Tests;

public sealed class PlanCatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private static Business Shop(LicenseCheck? check = null)
    {
        var b = Business.Create("01JBUSINESSPLANCAT000000001", "Ama Dressmaking", null, Now.AddDays(-30));
        if (check is not null)
        {
            b.AttachLicense("protected", "…WXYZ", check, Now);
        }

        return b;
    }

    [Fact]
    public void DisplayName_for_known_entitlements()
    {
        Assert.Equal("Starter", PlanCatalog.DisplayName(PlanCatalog.ForCode(PlanCodes.Starter)!));
        Assert.Equal("Workshop", PlanCatalog.DisplayName(PlanCatalog.ForCode(PlanCodes.Workshop)!));
        Assert.Equal("Production", PlanCatalog.DisplayName(PlanCatalog.ForCode(PlanCodes.Production)!));
        Assert.Equal("Unlimited", PlanCatalog.DisplayName(PlanCatalog.Resolve(null, null, false, false).Entitlements));
    }

    [Theory]
    [InlineData("starter", PlanCodes.Starter, 2)]
    [InlineData("STARTER", PlanCodes.Starter, 2)]
    [InlineData("  workshop  ", PlanCodes.Workshop, 8)]
    [InlineData("production", PlanCodes.Production, 20)]
    public void ForCode_maps_known_plans(string input, string code, int maxStaff)
    {
        var e = PlanCatalog.ForCode(input);
        Assert.NotNull(e);
        Assert.Equal(code, e.Code);
        Assert.False(e.IsUnlimited);
        Assert.Equal(maxStaff, e.MaxStaffExcludingOwner);
        Assert.True(e.Sync);
        Assert.True(e.Website);
        Assert.True(e.Export);
        Assert.True(e.CloudBackup);
        Assert.True(e.ShopPhoneOnReceipts);
        Assert.True(e.MonthlySummaryEmail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pro")]
    [InlineData("free")]
    [InlineData("unlimited")]
    public void ForCode_returns_null_for_blank_or_unknown(string? input) =>
        Assert.Null(PlanCatalog.ForCode(input));

    [Fact]
    public void Starter_does_not_include_workshop_features()
    {
        var e = PlanCatalog.ForCode(PlanCodes.Starter)!;
        Assert.False(e.WeeklySummaryEmail);
        Assert.False(e.AssignmentAndDueEmails);
        Assert.False(e.Photos);
        Assert.False(e.PdfReceipts);
        Assert.False(e.ProfitAndMonthlyReports);
        Assert.False(e.ExtraPhotoStorage);
    }

    [Fact]
    public void Workshop_includes_reports_but_not_extra_photo_storage()
    {
        var e = PlanCatalog.ForCode(PlanCodes.Workshop)!;
        Assert.True(e.WeeklySummaryEmail);
        Assert.True(e.AssignmentAndDueEmails);
        Assert.True(e.Photos);
        Assert.True(e.PdfReceipts);
        Assert.True(e.ProfitAndMonthlyReports);
        Assert.False(e.ExtraPhotoStorage);
    }

    [Fact]
    public void Production_includes_extra_photo_storage()
    {
        var e = PlanCatalog.ForCode(PlanCodes.Production)!;
        Assert.True(e.Photos);
        Assert.True(e.ExtraPhotoStorage);
        Assert.Equal(20, e.MaxStaffExcludingOwner);
    }

    [Fact]
    public void Keyless_shop_is_unlimited()
    {
        var r = PlanCatalog.Resolve(Shop());
        Assert.Equal(PlanCodes.Unlimited, r.Entitlements.Code);
        Assert.True(r.Entitlements.IsUnlimited);
        Assert.Null(r.Entitlements.MaxStaffExcludingOwner);
        Assert.False(r.WasUnknownPlan);
        Assert.Null(r.StoredPlan);
    }

    [Fact]
    public void Complimentary_is_unlimited_even_when_plan_is_starter()
    {
        var r = PlanCatalog.Resolve(Shop(new LicenseCheck(
            true, "valid", Plan: PlanCodes.Starter, IsComplimentary: true)));
        Assert.Equal(PlanCodes.Unlimited, r.Entitlements.Code);
        Assert.True(r.Entitlements.IsUnlimited);
        Assert.Equal(PlanCodes.Starter, r.StoredPlan);
        Assert.False(r.WasUnknownPlan);
    }

    [Fact]
    public void Trial_acts_as_starter_even_when_key_carries_production()
    {
        var r = PlanCatalog.Resolve(Shop(new LicenseCheck(
            true, "valid", Plan: PlanCodes.Production, IsTrial: true)));
        Assert.Equal(PlanCodes.Starter, r.Entitlements.Code);
        Assert.Equal(2, r.Entitlements.MaxStaffExcludingOwner);
        Assert.False(r.Entitlements.Photos);
        Assert.Equal(PlanCodes.Production, r.StoredPlan);
        Assert.False(r.WasUnknownPlan);
    }

    [Fact]
    public void Complimentary_wins_over_trial()
    {
        var r = PlanCatalog.Resolve(Shop(new LicenseCheck(
            true, "valid", Plan: PlanCodes.Workshop, IsTrial: true, IsComplimentary: true)));
        Assert.True(r.Entitlements.IsUnlimited);
        Assert.False(r.WasUnknownPlan);
    }

    [Theory]
    [InlineData(PlanCodes.Starter, 2, false)]
    [InlineData(PlanCodes.Workshop, 8, true)]
    [InlineData(PlanCodes.Production, 20, true)]
    public void Known_paid_plan_uses_catalog_row(string plan, int maxStaff, bool photos)
    {
        var r = PlanCatalog.Resolve(Shop(new LicenseCheck(true, "valid", Plan: plan)));
        Assert.Equal(plan, r.Entitlements.Code);
        Assert.Equal(maxStaff, r.Entitlements.MaxStaffExcludingOwner);
        Assert.Equal(photos, r.Entitlements.Photos);
        Assert.False(r.WasUnknownPlan);
        Assert.Equal(plan, r.StoredPlan);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Pro")]
    [InlineData("enterprise")]
    public void Unknown_or_blank_plan_on_licensed_shop_fails_closed_to_starter(string? plan)
    {
        var r = PlanCatalog.Resolve(Shop(new LicenseCheck(true, "valid", Plan: plan)));
        Assert.Equal(PlanCodes.Starter, r.Entitlements.Code);
        Assert.Equal(2, r.Entitlements.MaxStaffExcludingOwner);
        Assert.True(r.WasUnknownPlan);
        Assert.Equal(string.IsNullOrWhiteSpace(plan) ? null : plan.Trim(), r.StoredPlan);
    }

    [Theory]
    [InlineData("ѕtarter")] // Cyrillic 'ѕ' lookalike — must not match starter
    [InlineData("starter\u200B")] // zero-width space
    [InlineData("starter\0")]
    [InlineData("🚀")]
    public void Unicode_control_or_lookalike_plan_fails_closed_to_starter(string plan)
    {
        var r = PlanCatalog.Resolve("protected", plan, isTrial: false, isComplimentary: false);
        Assert.Equal(PlanCodes.Starter, r.Entitlements.Code);
        Assert.True(r.WasUnknownPlan);
        Assert.Null(PlanCatalog.ForCode(plan));
    }

    [Fact]
    public void Keyless_fields_ignore_plan_string_for_entitlements()
    {
        var r = PlanCatalog.Resolve(
            licenseKeyProtected: null,
            plan: PlanCodes.Production,
            isTrial: false,
            isComplimentary: false);
        Assert.True(r.Entitlements.IsUnlimited);
        Assert.Equal(PlanCodes.Unlimited, r.Entitlements.Code);
        Assert.Equal(PlanCodes.Production, r.StoredPlan);
        Assert.False(r.WasUnknownPlan);
    }

    [Fact]
    public void Whitespace_only_protected_key_is_treated_as_licensed_fail_closed()
    {
        // Matches SubscriptionRules (IsNullOrEmpty): whitespace is not "no key".
        // AttachLicense cannot store whitespace; this guards a corrupt/manual field path.
        var r = PlanCatalog.Resolve("   ", plan: "enterprise", isTrial: false, isComplimentary: false);
        Assert.False(r.Entitlements.IsUnlimited);
        Assert.Equal(PlanCodes.Starter, r.Entitlements.Code);
        Assert.True(r.WasUnknownPlan);
    }

    [Fact]
    public void Resolve_fields_overload_matches_business_overload()
    {
        var b = Shop(new LicenseCheck(true, "valid", Plan: "  Workshop ", IsTrial: false));
        var fromBusiness = PlanCatalog.Resolve(b);
        var fromFields = PlanCatalog.Resolve(
            b.LicenseKeyProtected, b.LicensePlan, b.LicenseIsTrial, b.LicenseIsComplimentary);
        Assert.Equal(fromBusiness, fromFields);
        Assert.Equal(PlanCodes.Workshop, fromFields.Entitlements.Code);
    }
}
