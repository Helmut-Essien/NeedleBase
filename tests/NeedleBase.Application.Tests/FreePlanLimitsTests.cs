using NeedleBase.Application.Licensing;

namespace NeedleBase.Application.Tests;

public class FreePlanLimitsTests
{
    [Fact]
    public void Caps_MatchPublishedFreePlan()
    {
        Assert.Equal(10, FreePlanLimits.MaxCustomersEver);
        Assert.Equal(20, FreePlanLimits.MaxOrdersEver);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    public void CustomerLimitMessage_StatesWhenFull(int used, bool full)
    {
        var message = FreePlanLimits.CustomerLimitMessage(used);
        Assert.Contains("10", message);
        Assert.Equal(full, message.Contains("Delete does not free", StringComparison.Ordinal));
    }

    [Fact]
    public void UsageMeters_AreCompact()
    {
        Assert.Equal("7/10 customers", FreePlanLimits.CustomerMeter(7));
        Assert.Equal("3/20 orders", FreePlanLimits.OrderMeter(3));
        Assert.Equal("7/10 customers · 3/20 orders", FreePlanLimits.UsageMeters(7, 3));
        Assert.Equal("10/10 customers", FreePlanLimits.CustomerMeter(99));
        Assert.Equal("0/10 customers", FreePlanLimits.CustomerMeter(-3));
        Assert.Equal("0/20 orders", FreePlanLimits.OrderMeter(int.MinValue));
    }
}
