using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Orders;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Tests;

/// <summary>Owner/Manager order edit rules shared by API, sync and the phone.</summary>
public class OrderEditorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Taken = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static Order NewOrder(decimal price = 500m)
    {
        var line = OrderLine.Create("01LINE0000000000000000000A", "biz", "ord", GarmentTemplate.Dress, 1, price, null,
            MeasurementUnit.Centimetres, """{"bust":92}""", null, 0, Taken);
        return Order.Create("ord", "biz", "cust", null, null, null, null, [line], Now);
    }

    private static CreateOrderLineRequest Keep(Order o, int qty, decimal price) => new()
    {
        Id = o.Lines.First().Id, GarmentTemplate = GarmentTemplate.Dress, Quantity = qty, UnitPrice = price, Unit = MeasurementUnit.Centimetres
    };

    [Theory]
    [InlineData(BusinessRole.Owner, true)]
    [InlineData(BusinessRole.Manager, true)]
    [InlineData(BusinessRole.Tailor, false)]
    [InlineData(null, false)]
    public void CanEdit_OwnerAndManagerOnly(string? role, bool expected) => Assert.Equal(expected, OrderEditor.CanEdit(role));

    [Fact]
    public void Apply_RevisesExistingLine_KeepsSnapshot()
    {
        var order = NewOrder();
        var (error, _) = OrderEditor.Apply(order, new UpdateOrderRequest { Notes = " hem ", Lines = [Keep(order, 2, 300m)] }, [], 0m, Now);
        Assert.Equal(OrderError.None, error);
        Assert.Equal(600m, order.TotalAmount);
        Assert.Equal("hem", order.Notes);
        Assert.Equal(Taken, order.Lines.Single().MeasurementTakenAtUtc);
    }

    [Fact]
    public void Apply_NewLine_SnapshotsLatestProfileForThatGarment()
    {
        var order = NewOrder();
        var older = MeasurementProfile.Create("p1", "biz", "cust", GarmentTemplate.Shirt, MeasurementUnit.Centimetres, """{"chest":90}""", null, Taken, Taken);
        var latest = MeasurementProfile.Create("p2", "biz", "cust", GarmentTemplate.Shirt, MeasurementUnit.Centimetres, """{"chest":95}""", null, Now.AddDays(-1), Now.AddDays(-1));
        var request = new UpdateOrderRequest
        {
            Lines = [Keep(order, 1, 500m), new CreateOrderLineRequest { GarmentTemplate = GarmentTemplate.Shirt, Quantity = 1, UnitPrice = 150m, Unit = MeasurementUnit.Centimetres }]
        };

        Assert.Equal(OrderError.None, OrderEditor.Apply(order, request, [older, latest], 0m, Now).Error);
        var shirt = order.Lines.Single(l => l.GarmentTemplate == GarmentTemplate.Shirt);
        Assert.Contains("95", shirt.MeasurementFieldsJson);
        Assert.Equal(Now.AddDays(-1), shirt.MeasurementTakenAtUtc);
        Assert.Equal(650m, order.TotalAmount);
    }

    [Fact]
    public void Apply_NewLine_NoProfile_HasNoSnapshot()
    {
        var order = NewOrder();
        var request = new UpdateOrderRequest
        {
            Lines = [new CreateOrderLineRequest { GarmentTemplate = GarmentTemplate.Shirt, Quantity = 1, UnitPrice = 150m, Unit = MeasurementUnit.Centimetres }]
        };
        Assert.Equal(OrderError.None, OrderEditor.Apply(order, request, [], 0m, Now).Error);
        var line = Assert.Single(order.Lines);
        Assert.Null(line.MeasurementTakenAtUtc);
    }

    [Fact]
    public void Apply_BelowPaid_IsRefused_AndOrderUntouched()
    {
        var order = NewOrder();
        var (error, _) = OrderEditor.Apply(order, new UpdateOrderRequest { Notes = "x", Lines = [Keep(order, 1, 200m)] }, [], 300m, Now);
        Assert.Equal(OrderError.BelowPaid, error);
        Assert.Equal(500m, order.TotalAmount);
        Assert.Null(order.Notes);
    }

    [Fact]
    public void Apply_RaisingTotal_WhileAlreadyOverpaid_IsAllowed()
    {
        var order = NewOrder();
        Assert.Equal(OrderError.None, OrderEditor.Apply(order, new UpdateOrderRequest { Lines = [Keep(order, 1, 550m)] }, [], 600m, Now).Error);
    }

    [Fact]
    public void Apply_Locked_IsRefused()
    {
        var order = NewOrder();
        order.SetStatus(OrderStatus.Cancelled, Now);
        Assert.Equal(OrderError.Locked, OrderEditor.Apply(order, new UpdateOrderRequest { Notes = "x" }, [], 0m, Now).Error);
    }

    [Fact]
    public void Apply_InvalidLines_AreRefused()
    {
        var order = NewOrder();
        Assert.Equal(OrderError.Invalid, OrderEditor.Apply(order, new UpdateOrderRequest { Lines = [] }, [], 0m, Now).Error);
        Assert.Equal(OrderError.Invalid, OrderEditor.Apply(order, new UpdateOrderRequest { Lines = [Keep(order, 0, 100m)] }, [], 0m, Now).Error);
        Assert.Equal(OrderError.Invalid, OrderEditor.Apply(order, new UpdateOrderRequest { Lines = [Keep(order, 1, 100m), Keep(order, 1, 100m)] }, [], 0m, Now).Error);
        Assert.Equal(OrderError.TotalMismatch, OrderEditor.Apply(order, new UpdateOrderRequest { TotalAmount = 1m, Lines = [Keep(order, 1, 100m)] }, [], 0m, Now).Error);
        Assert.Equal(500m, order.TotalAmount);
    }

    [Fact]
    public void LinesDiffer_DetectsChanges()
    {
        var order = NewOrder();
        Assert.False(OrderEditor.LinesDiffer(order, [Keep(order, 1, 500m)]));
        Assert.True(OrderEditor.LinesDiffer(order, [Keep(order, 2, 500m)]));
    }

    [Fact]
    public void NewErrorCodes_HaveFriendlyCopy_AndMap()
    {
        Assert.Equal(OrderErrorMessages.TotalBelowPaid, OrderErrorMapping.Code(OrderError.BelowPaid));
        Assert.Equal(OrderErrorMessages.EditForbidden, OrderErrorMapping.Code(OrderError.Forbidden));
        Assert.DoesNotContain("_", OrderErrorMessages.For(OrderErrorMessages.TotalBelowPaid));
    }

    [Fact]
    public void LineDto_MeasurementDisplay()
    {
        var withValues = new OrderLineDto("l", GarmentTemplate.Dress, 1, 1m, 1m, null, MeasurementUnit.Centimetres,
            new Dictionary<string, decimal?> { ["bust"] = 92m }, null, 0, Taken);
        Assert.True(withValues.HasMeasurements);
        Assert.StartsWith("Taken ", withValues.MeasurementTakenDisplay);
        Assert.Contains(withValues.MeasurementValues, v => v.Value.StartsWith("92"));

        var old = withValues with { MeasurementTakenAtUtc = null };
        Assert.Equal("Saved with this order (date not recorded)", old.MeasurementTakenDisplay);

        var none = withValues with { MeasurementFields = new Dictionary<string, decimal?>(), MeasurementTakenAtUtc = null };
        Assert.True(none.HasNoMeasurements);
        Assert.Equal("No measurements saved with this order", none.MeasurementTakenDisplay);
    }

    [Fact]
    public void ProfileNeeds_KeptLinesNeedNothing()
    {
        var order = NewOrder();
        var (ids, garments) = OrderEditor.ProfileNeeds(order, [Keep(order, 3, 200m)]);
        Assert.Empty(ids);
        Assert.Empty(garments);
    }

    [Fact]
    public void ProfileNeeds_NewLines_NamedProfileOrGarmentButNotTypedFields()
    {
        var order = NewOrder();
        var (ids, garments) = OrderEditor.ProfileNeeds(order,
        [
            new CreateOrderLineRequest { GarmentTemplate = " Shirt ", Quantity = 1, UnitPrice = 1m },
            new CreateOrderLineRequest { GarmentTemplate = "shirt", Quantity = 1, UnitPrice = 1m },
            new CreateOrderLineRequest { GarmentTemplate = "dress", MeasurementProfileId = " p9 ", Quantity = 1, UnitPrice = 1m },
            new CreateOrderLineRequest { GarmentTemplate = "trousers", MeasurementFields = new() { ["waist"] = 80m }, Quantity = 1, UnitPrice = 1m },
        ]);
        Assert.Equal(["p9"], ids);
        Assert.Equal(["shirt"], garments);
    }
}
