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
}
