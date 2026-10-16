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
}
