using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Orders;
using NeedleBase.Domain.Exceptions;

namespace NeedleBase.Application.Tests;

public class OrderSupportTests
{
    [Theory]
    [InlineData(OrderErrorMessages.OrderLocked)]
    [InlineData(OrderErrorMessages.OrderStatusInvalid)]
    [InlineData(OrderErrorMessages.AssigneeInvalid)]
    [InlineData(OrderErrorMessages.TotalMismatch)]
    [InlineData(OrderErrorMessages.Validation)]
    public void OrderErrorMessages_HaveFriendlyCopy(string code)
    {
        var message = OrderErrorMessages.For(code);
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain("_", message);
    }

    [Fact]
    public void FromException_MapsDomainCodes()
    {
        Assert.Equal(OrderError.Locked,
            OrderLineFactory.FromException(new DomainRuleException(DomainErrorCodes.OrderLocked, "x")));
        Assert.Equal(OrderError.StatusInvalid,
            OrderLineFactory.FromException(new DomainRuleException(DomainErrorCodes.OrderStatusInvalid, "x")));
        Assert.Equal(OrderError.Invalid, OrderLineFactory.FromException(new ArgumentException("x")));
    }

    [Fact]
    public void BusinessToday_UsesAccraCalendar()
    {
        var lateUtc = new DateTimeOffset(2026, 10, 20, 23, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 10, 20), BusinessTime.Today(lateUtc));
    }

    [Theory]
    [InlineData("  #ab12cd34 ", "AB12CD34")]
    [InlineData("ab12", "AB12")]
    public void SearchText_Ref_NormalisesTerm(string input, string expected) =>
        Assert.Equal(expected, SearchText.Ref(input));
}
