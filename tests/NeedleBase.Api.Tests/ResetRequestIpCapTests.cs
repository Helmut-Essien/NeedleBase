using NeedleBase.Application.Abstractions;
using NeedleBase.Infrastructure.Identity;
using Xunit;

namespace NeedleBase.Api.Tests;

public class ResetRequestIpCapTests
{
    private sealed class StepClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public void Cap_AllowsPerHour_ThenBlocks_PerIp_AndResetsAfterWindow()
    {
        var clock = new StepClock();
        var cap = new ResetRequestIpCap(clock);
        for (var i = 0; i < ResetRequestIpCap.PerHour; i++)
        {
            Assert.Null(cap.TryTake("10.0.0.1"));
        }

        var wait = cap.TryTake("10.0.0.1");
        Assert.NotNull(wait);
        Assert.True(wait > TimeSpan.Zero && wait <= ResetRequestIpCap.Window);
        Assert.Null(cap.TryTake("10.0.0.2"));

        clock.UtcNow += ResetRequestIpCap.Window;
        Assert.Null(cap.TryTake("10.0.0.1"));
    }

    [Fact]
    public void Cap_TreatsMissingIp_AsOneBucket()
    {
        var cap = new ResetRequestIpCap(new StepClock());
        for (var i = 0; i < ResetRequestIpCap.PerHour; i++)
        {
            Assert.Null(cap.TryTake(i % 2 == 0 ? null : " "));
        }

        Assert.NotNull(cap.TryTake(null));
    }
}
