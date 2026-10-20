using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Domain.Exceptions;

namespace NeedleBase.Domain.Tests;

public class OrderTests
{
    [Fact]
    public void Order_Create_SetsDisplayRefAndNewStatus()
    {
        var now = DateTimeOffset.UtcNow;
        var id = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
        var line = OrderLine.Create(
            "01ARZ3NDEKTSV4RRFFQ69G5FA1",
            "biz1",
            id,
            GarmentTemplate.Dress,
            1,
            800m,
            null,
            MeasurementUnit.Centimetres,
            """{"bust":92}""",
            null);

        var order = Order.Create(
            id,
            "biz1",
            "cust1",
            DateOnly.FromDateTime(now.UtcDateTime.AddDays(7)),
            null,
            null,
            "kente slit",
            [line],
            now);

        Assert.Equal(OrderStatus.New, order.Status);
        Assert.Equal(Order.BuildDisplayRef(id), order.DisplayRef);
        Assert.Equal(800m, order.TotalAmount);
        Assert.Single(order.Lines);
        Assert.Equal("Ready to collect", OrderStatus.DisplayName(OrderStatus.Ready));
    }

    [Fact]
    public void Order_Create_RequiresAtLeastOneLine()
    {
        Assert.Throws<ArgumentException>(() =>
            Order.Create(
                "id1",
                "biz1",
                "cust1",
                null,
                null,
                null,
                null,
                Array.Empty<OrderLine>(),
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Order_SetStatus_RejectsChangeAfterCollected()
    {
        var order = CreateSample();
        order.SetStatus(OrderStatus.Collected, DateTimeOffset.UtcNow);

        var ex = Assert.Throws<DomainRuleException>(() =>
            order.SetStatus(OrderStatus.Sewing, DateTimeOffset.UtcNow));
        Assert.Equal(DomainErrorCodes.OrderLocked, ex.Code);
    }

    [Fact]
    public void Order_UpdateHeader_RejectsWhenCollected()
    {
        var order = CreateSample();
        order.SetStatus(OrderStatus.Collected, DateTimeOffset.UtcNow);

        var ex = Assert.Throws<DomainRuleException>(() =>
            order.UpdateHeader(null, null, null, null, DateTimeOffset.UtcNow));
        Assert.Equal(DomainErrorCodes.OrderLocked, ex.Code);
    }

    [Fact]
    public void Order_SetStatus_UnknownStatus_HasStatusInvalidCode()
    {
        var order = CreateSample();
        var ex = Assert.Throws<DomainRuleException>(() => order.SetStatus("Shipped", DateTimeOffset.UtcNow));
        Assert.Equal(DomainErrorCodes.OrderStatusInvalid, ex.Code);
    }

    [Fact]
    public void Order_TotalAmount_IsSumOfLineTotals()
    {
        var id = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
        var a = OrderLine.Create("L1", "biz1", id, GarmentTemplate.Shirt, 2, 150.5m, null, MeasurementUnit.Centimetres, "{}", null);
        var b = OrderLine.Create("L2", "biz1", id, GarmentTemplate.Dress, 1, 300m, null, MeasurementUnit.Centimetres, "{}", null);

        var order = Order.Create(id, "biz1", "cust1", null, null, null, null, [a, b], DateTimeOffset.UtcNow);

        Assert.Equal(601m, order.TotalAmount);
    }

    [Fact]
    public void Order_DisplayRef_IsEightUpperCaseChars()
    {
        Assert.Equal("Q69G5FAV", Order.BuildDisplayRef("01ARZ3NDEKTSV4RRFFQ69g5fav"));
        var random = Order.NewRandomDisplayRef();
        Assert.Equal(8, random.Length);
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{8}$", random);
    }

    [Fact]
    public void OrderStatus_IsOverdue_WhenDuePassedAndNotTerminal()
    {
        var today = new DateOnly(2026, 10, 20);
        Assert.True(OrderStatus.IsOverdue(OrderStatus.Sewing, new DateOnly(2026, 10, 19), today));
        Assert.False(OrderStatus.IsOverdue(OrderStatus.Collected, new DateOnly(2026, 10, 19), today));
        Assert.False(OrderStatus.IsOverdue(OrderStatus.New, null, today));
    }

    [Fact]
    public void OrderLine_RejectsInvalidQuantity()
    {
        Assert.Throws<ArgumentException>(() =>
            OrderLine.Create(
                "id1",
                "biz1",
                "ord1",
                GarmentTemplate.Shirt,
                0,
                10m,
                null,
                MeasurementUnit.Centimetres,
                "{}",
                null));
    }

    private static Order CreateSample()
    {
        var id = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
        var line = OrderLine.Create(
            "01ARZ3NDEKTSV4RRFFQ69G5FA1",
            "biz1",
            id,
            GarmentTemplate.Shirt,
            1,
            100m,
            null,
            MeasurementUnit.Centimetres,
            "{}",
            null);
        return Order.Create(id, "biz1", "cust1", null, null, null, null, [line], DateTimeOffset.UtcNow);
    }
}
