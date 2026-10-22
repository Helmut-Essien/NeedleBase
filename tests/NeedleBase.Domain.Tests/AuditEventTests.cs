using NeedleBase.Domain.Entities;

namespace NeedleBase.Domain.Tests;

public class AuditEventTests
{
    [Fact]
    public void LongSummary_IsCut_WithoutSplittingAnEmoji()
    {
        var summary = new string('a', 298) + "😀😀"; // the cut point lands inside the first emoji
        var e = AuditEvent.Create("01J0000000000000000000000A", "b1", "customer.created", summary, DateTimeOffset.UtcNow);
        Assert.True(e.Summary.Length <= 300);
        Assert.EndsWith("…", e.Summary);
        Assert.DoesNotContain(e.Summary, char.IsHighSurrogate); // the half emoji was dropped
    }

    [Fact]
    public void UnknownSource_BecomesSystem_AndBlankFieldsBecomeNull()
    {
        var e = AuditEvent.Create("01J0000000000000000000000B", null, "auth.login_failed", "  ", DateTimeOffset.UtcNow,
            source: "hacker", userAgent: "   ", deviceId: "");
        Assert.Equal(AuditSource.System, e.Source);
        Assert.Equal("auth.login_failed", e.Summary);
        Assert.Null(e.UserAgent);
        Assert.Null(e.DeviceId);
    }
}
