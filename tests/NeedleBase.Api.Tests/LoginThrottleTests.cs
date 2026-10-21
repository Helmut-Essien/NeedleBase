using NeedleBase.Application.Abstractions;
using NeedleBase.Infrastructure.Identity;

namespace NeedleBase.Api.Tests;

public class LoginThrottleTests
{
    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    [Fact]
    public void Throttle_IsPerEmailAndIp_Progressive_AndForgetsAfterAnHour()
    {
        var clock = new Clock();
        var throttle = new LoginThrottle(clock);
        for (var i = 0; i < LoginThrottle.FreeFailures - 1; i++)
        {
            throttle.RecordFailure("Owner@Shop.test", "10.0.0.1");
            Assert.Null(throttle.RetryAfter("owner@shop.test", "10.0.0.1"));
        }

        throttle.RecordFailure("owner@shop.test", "10.0.0.1");
        Assert.Equal(LoginThrottle.FirstDelay, throttle.RetryAfter("owner@shop.test", "10.0.0.1"));
        Assert.Null(throttle.RetryAfter("owner@shop.test", "10.0.0.2"));    // the real owner elsewhere is never blocked
        Assert.Null(throttle.RetryAfter("other@shop.test", "10.0.0.1"));

        clock.UtcNow += LoginThrottle.FirstDelay;
        throttle.RecordFailure("owner@shop.test", "10.0.0.1");
        Assert.Equal(LoginThrottle.FirstDelay * 2, throttle.RetryAfter("owner@shop.test", "10.0.0.1"));

        for (var i = 0; i < 20; i++)
        {
            throttle.RecordFailure("owner@shop.test", "10.0.0.1");
        }

        Assert.Equal(LoginThrottle.MaxDelay, throttle.RetryAfter("owner@shop.test", "10.0.0.1"));

        clock.UtcNow += LoginThrottle.Forget;
        Assert.Null(throttle.RetryAfter("owner@shop.test", "10.0.0.1"));

        throttle.RecordFailure("owner@shop.test", "10.0.0.1");
        throttle.RecordSuccess("owner@shop.test", "10.0.0.1");
        Assert.Null(throttle.RetryAfter("owner@shop.test", "10.0.0.1"));
    }
}
