using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Domain.Tests;

public class CustomerTests
{
    [Fact]
    public void Customer_Create_TrimsAndSetsBusinessId()
    {
        var now = DateTimeOffset.UtcNow;
        var customer = Customer.Create(
            "01ARZ3NDEKTSV4RRFFQ69G5FAV",
            "01ARZ3NDEKTSV4RRFFQ69G5FAW",
            "  Efua Mensah  ",
            " +233245550123 ",
            "  prefers kente  ",
            null,
            now);

        Assert.Equal("Efua Mensah", customer.Name);
        Assert.Equal("+233245550123", customer.Phone);
        Assert.Equal("prefers kente", customer.Notes);
        Assert.Equal("01ARZ3NDEKTSV4RRFFQ69G5FAW", customer.BusinessId);
        Assert.Equal(now, customer.CreatedAtUtc);
    }

    [Fact]
    public void Customer_Create_RequiresNameAndPhone()
    {
        Assert.Throws<ArgumentException>(() =>
            Customer.Create("id1", "biz1", " ", "+233", null, null, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() =>
            Customer.Create("id1", "biz1", "Efua", " ", null, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MeasurementProfile_Create_RequiresValidTemplate()
    {
        Assert.Throws<ArgumentException>(() =>
            MeasurementProfile.Create(
                "id1",
                "biz1",
                "cust1",
                "unknown",
                MeasurementUnit.Centimetres,
                "{}",
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MeasurementProfile_Create_DressTemplate_Succeeds()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = MeasurementProfile.Create(
            "01ARZ3NDEKTSV4RRFFQ69G5FAX",
            "01ARZ3NDEKTSV4RRFFQ69G5FAW",
            "01ARZ3NDEKTSV4RRFFQ69G5FAY",
            GarmentTemplate.Dress,
            MeasurementUnit.Centimetres,
            """{"bust":92,"waist":70}""",
            null,
            now,
            now);

        Assert.Equal(GarmentTemplate.Dress, profile.GarmentTemplate);
        Assert.Equal(MeasurementUnit.Centimetres, profile.Unit);
    }
}
