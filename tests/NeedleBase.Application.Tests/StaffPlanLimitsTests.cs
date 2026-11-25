using NeedleBase.Application.Licensing;
using NeedleBase.Application.Staff;

namespace NeedleBase.Application.Tests;

public sealed class StaffPlanLimitsTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, true)]
    [InlineData(2, 0, false)]
    [InlineData(1, 1, false)]
    [InlineData(0, 2, false)]
    [InlineData(0, 1, true)]
    public void Starter_cap_of_two(int active, int pending, bool expected) =>
        Assert.Equal(expected, StaffPlanLimits.CanAddOne(PlanCatalog.ForCode(PlanCodes.Starter)!, active, pending));

    [Fact]
    public void Unlimited_always_allows()
    {
        var unlimited = PlanCatalog.Resolve(licenseKeyProtected: null, plan: null, isTrial: false, isComplimentary: false)
            .Entitlements;
        Assert.True(StaffPlanLimits.CanAddOne(unlimited, 100, 50));
    }

    [Fact]
    public void Workshop_allows_eight()
    {
        var e = PlanCatalog.ForCode(PlanCodes.Workshop)!;
        Assert.True(StaffPlanLimits.CanAddOne(e, 7, 0));
        Assert.False(StaffPlanLimits.CanAddOne(e, 8, 0));
        Assert.False(StaffPlanLimits.CanAddOne(e, 7, 1));
    }

    [Fact]
    public void Negative_counts_refuse() =>
        Assert.False(StaffPlanLimits.CanAddOne(PlanCatalog.ForCode(PlanCodes.Starter)!, -1, 0));
}
