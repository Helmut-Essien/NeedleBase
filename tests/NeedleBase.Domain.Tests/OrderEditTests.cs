using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Domain.Exceptions;

namespace NeedleBase.Domain.Tests;

public class OrderEditTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Taken = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static OrderLine Line(string id, decimal price, int qty = 1, DateTimeOffset? taken = null) =>
        OrderLine.Create(id, "biz", "ord", GarmentTemplate.Dress, qty, price, null, MeasurementUnit.Centimetres, """{"bust":92}""", null, 0, taken);

    private static Order NewOrder(params OrderLine[] lines) =>
        Order.Create("ord", "biz", "cust", null, null, null, null, lines, Now);

    [Fact]
    public void Revise_ChangesQtyPriceNotes_KeepsGarmentAndSnapshot()
    {
        var line = Line("l1", 100m, taken: Taken);
        line.Revise(3, 120m, "  longer sleeves ");
        Assert.Equal(3, line.Quantity);
        Assert.Equal(360m, line.LineTotal);
        Assert.Equal("longer sleeves", line.Notes);
        Assert.Equal(GarmentTemplate.Dress, line.GarmentTemplate);
        Assert.Equal(Taken, line.MeasurementTakenAtUtc);
        Assert.Contains("92", line.MeasurementFieldsJson);
    }

    [Fact]
    public void ReviseLines_RemovesAndAdds_RecomputesTotal()
    {
        var a = Line("l1", 100m);
        var b = Line("l2", 200m);
        var order = NewOrder(a, b);
        var c = Line("l3", 50m, 2);

        order.ReviseLines([c, a], Now.AddMinutes(5));

        Assert.Equal(200m, order.TotalAmount);
        Assert.Equal(2, order.Lines.Count);
        Assert.DoesNotContain(order.Lines, l => l.Id == "l2");
        Assert.Equal(0, order.Lines.Single(l => l.Id == "l3").SortOrder);
        Assert.Equal(Now.AddMinutes(5), order.DateModifiedUtc);
    }

    [Fact]
    public void ReviseLines_Empty_Throws()
    {
        var order = NewOrder(Line("l1", 100m));
        Assert.Throws<ArgumentException>(() => order.ReviseLines([], Now));
    }

    [Theory]
    [InlineData(OrderStatus.Collected)]
    [InlineData(OrderStatus.Cancelled)]
    public void LockedOrder_RefusesEdits(string status)
    {
        var line = Line("l1", 100m);
        var order = NewOrder(line);
        if (status == OrderStatus.Collected)
        {
            order.SetStatus(OrderStatus.Ready, Now);
        }

        order.SetStatus(status, Now);
        Assert.Throws<DomainRuleException>(() => order.ReviseLines([line], Now));
        Assert.Throws<DomainRuleException>(() => order.UpdateHeader(null, null, null, "x", Now));
    }
}

public class OrderReassignTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private static Order NewOrder() =>
        Order.Create("ord", "biz", "cust", null, null, "u1", "keep me",
            [OrderLine.Create("l1", "biz", "ord", GarmentTemplate.Dress, 1, 100m, null, MeasurementUnit.Centimetres, """{"bust":92}""", null, 0, null)], Now);

    [Fact]
    public void Reassign_changes_only_the_assignee_and_bumps_modified()
    {
        var order = NewOrder();
        order.Reassign("u2", Now.AddHours(1));
        Assert.Equal("u2", order.AssignedToUserId);
        Assert.Equal("keep me", order.Notes);
        Assert.Equal(Now.AddHours(1), order.DateModifiedUtc);
        order.Reassign(null, Now.AddHours(2));
        Assert.Null(order.AssignedToUserId);
    }

    [Fact]
    public void Closed_order_cannot_be_reassigned()
    {
        var order = NewOrder();
        order.SetStatus(OrderStatus.Cancelled, Now);
        Assert.Throws<DomainRuleException>(() => order.Reassign("u2", Now));
    }
}
