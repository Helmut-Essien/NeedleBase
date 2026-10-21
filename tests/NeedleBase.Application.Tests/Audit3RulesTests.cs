using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Orders;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Tests;

/// <summary>Third-audit rules: lenient sync, changed-only measurement checks, 118 in, page cap, stale updates.</summary>
public class Audit3RulesTests
{
    [Fact]
    public void OrderLines_Lenient_SkipsInputLimits_ButKeepsDomainInvariants()
    {
        var many = Enumerable.Range(0, AmountRules.OrderLinesMax + 5).Select(_ => Line(null, 2_000_000m)).ToList();
        var (lines, error) = OrderLineFactory.Build("biz", "order", many, new Dictionary<string, MeasurementProfile>(), lenient: true);
        Assert.Equal(OrderError.None, error);
        Assert.Equal(AmountRules.OrderLinesMax + 5, lines!.Count);

        // Profile gone and no snapshot: a synced line keeps going without measurements instead of failing forever.
        var (bare, bareError) = OrderLineFactory.Build("biz", "order", [Line("01MISSINGPROFILE0000000001", 10m)],
            new Dictionary<string, MeasurementProfile>(), lenient: true);
        Assert.Equal(OrderError.None, bareError);
        Assert.Null(Assert.Single(bare!).MeasurementProfileId);

        Assert.Throws<ArgumentException>(() => OrderLineFactory.Build("biz", "order", [Line(null, -1m)],
            new Dictionary<string, MeasurementProfile>(), lenient: true));
    }

    [Fact]
    public void ChangedMeasurements_OnlyEditedValuesAreChecked_AllWhenUnitChanges()
    {
        var stored = new Dictionary<string, decimal?> { ["chest"] = 400m, ["waist"] = 80m };

        AmountRules.EnsureChangedMeasurements(MeasurementUnit.Centimetres,
            new Dictionary<string, decimal?> { ["chest"] = 400m, ["waist"] = 82m }, MeasurementUnit.Centimetres, stored);
        Assert.Throws<ArgumentException>(() => AmountRules.EnsureChangedMeasurements(MeasurementUnit.Centimetres,
            new Dictionary<string, decimal?> { ["chest"] = 401m, ["waist"] = 80m }, MeasurementUnit.Centimetres, stored));
        Assert.Throws<ArgumentException>(() => AmountRules.EnsureChangedMeasurements(MeasurementUnit.Centimetres,
            new Dictionary<string, decimal?> { ["chest"] = 400m, ["hip"] = 0m }, MeasurementUnit.Centimetres, stored));
        Assert.Throws<ArgumentException>(() => AmountRules.EnsureChangedMeasurements(MeasurementUnit.Inches,
            new Dictionary<string, decimal?> { ["chest"] = 400m }, MeasurementUnit.Centimetres, stored));
    }

    [Fact]
    public void Inches_Cap_Is118_SamePhysicalCapAsCentimetres()
    {
        Assert.Equal(118m, AmountRules.MaxInches);
        Assert.True(AmountRules.MaxInches * 2.54m <= AmountRules.MaxCentimetres);
    }

    [Theory]
    [InlineData(int.MaxValue, 100)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(50_000_000, 100)]
    public void Pagination_HugePage_IsCapped_SoOffsetCannotOverflow(int page, int pageSize)
    {
        var (p, size) = PaginatedResponse<int>.Normalize(page, pageSize);
        Assert.True((long)(p - 1) * size <= int.MaxValue);
        Assert.InRange((p - 1) * size, 0, int.MaxValue);
    }

    [Fact]
    public void StaleUpdate_NullBaseIsNeverStale_OlderBaseIs()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(StaleUpdate.IsStale(null, now));
        Assert.False(StaleUpdate.IsStale(now, now));
        Assert.True(StaleUpdate.IsStale(now.AddSeconds(-5), now));
    }

    private static CreateOrderLineRequest Line(string? profileId, decimal price) => new()
    {
        GarmentTemplate = GarmentTemplate.Shirt,
        Quantity = 1,
        UnitPrice = price,
        MeasurementProfileId = profileId,
        Unit = MeasurementUnit.Centimetres,
        MeasurementFields = new Dictionary<string, decimal?>()
    };
}
