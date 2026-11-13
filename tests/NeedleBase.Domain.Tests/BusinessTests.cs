using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Domain.Tests;

public class BusinessTests
{
    [Fact]
    public void Business_Create_TrimsName()
    {
        var business = Business.Create("01ARZ3NDEKTSV4RRFFQ69G5FAV", "  Ama Stitches  ", null, DateTimeOffset.UtcNow);
        Assert.Equal("Ama Stitches", business.Name);
        Assert.Equal("GHS", business.CurrencyCode);
    }

    [Fact]
    public void StaffMembership_RejectsInvalidRole()
    {
        Assert.Throws<ArgumentException>(() =>
            StaffMembership.Create(
                "01ARZ3NDEKTSV4RRFFQ69G5FAV",
                "01ARZ3NDEKTSV4RRFFQ69G5FAW",
                "user-1",
                "Admin",
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BusinessRole_OwnerIsValid()
    {
        Assert.True(BusinessRole.IsValid(BusinessRole.Owner));
    }

    [Fact]
    public void Close_AnonymisesShopAndClearsLicence()
    {
        var now = DateTimeOffset.UtcNow;
        var business = Business.Create("01ARZ3NDEKTSV4RRFFQ69G5FAV", "Ama Stitches", "+233200000000", now);
        business.AttachLicense("protected-key", "ABCD", new LicenseCheck(true, "ok", Token: "tok"), now);

        business.Close(now.AddMinutes(1));

        Assert.True(business.IsClosed);
        Assert.Equal("Closed shop", business.Name);
        Assert.Null(business.Phone);
        Assert.Null(business.LicenseKeyProtected);
        Assert.Null(business.LicenseToken);
        Assert.False(business.LicenseIsValid);
        Assert.Equal("closed", business.LicenseReason);
        var closedAt = business.ClosedAtUtc;
        business.Close(now.AddHours(1));
        Assert.Equal(closedAt, business.ClosedAtUtc);
    }

    [Fact]
    public void CloseWithShop_AllowsOwnerMembershipToBeTurnedOff()
    {
        var membership = StaffMembership.Create(
            "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            "01ARZ3NDEKTSV4RRFFQ69G5FAW",
            "user-1",
            BusinessRole.Owner,
            DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(membership.Deactivate);
        membership.CloseWithShop();
        Assert.False(membership.IsActive);
    }
}
