using NeedleBase.Application.Audit;
using NeedleBase.Application.Contracts.Audit;

namespace NeedleBase.Application.Tests;

public class AuditMaskTests
{
    [Theory]
    [InlineData("+233245550123", "+233•••••0123")]
    [InlineData("0245550123", "•••••0123")]
    [InlineData("024 555 0303", "•••••0303")]
    [InlineData("12345", "•••••")]
    public void Phone_KeepsOnlyTheLastFourDigits(string phone, string expected) => Assert.Equal(expected, AuditMask.Phone(phone));

    [Theory]
    [InlineData("ama.owusu@gmail.com", "a•••@gmail.com")]
    [InlineData("not-an-email", "•••")]
    [InlineData("@x.com", "•••")]
    [InlineData(null, "•••")]
    public void Email_IsMasked(string? email, string expected) => Assert.Equal(expected, AuditMask.Email(email));

    [Fact]
    public void Short_CollapsesLines_AndNeverSplitsAnEmoji()
    {
        Assert.Equal("a b c", AuditMask.Short("a\r\n b\tc"));
        var cut = AuditMask.Short(new string('x', 58) + "👗👗", 60);
        Assert.Equal(new string('x', 58) + "…", cut);
        Assert.DoesNotContain(cut, char.IsHighSurrogate);
    }

    [Fact]
    public void FieldLabels_ArePlain()
    {
        Assert.Equal("due date", new AuditChangeDto("DueDate", null, "2026-10-30").FieldDisplay);
        Assert.Equal("assignee", AuditActions.FieldLabel("Assignee"));
        Assert.Equal("status", AuditActions.FieldLabel("Status"));
    }

    [Fact]
    public void EveryActionHasAGroup_AndADisplayName()
    {
        var actions = typeof(AuditActions).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.NotEmpty(actions);
        Assert.All(actions, a =>
        {
            Assert.Contains(AuditActions.Groups, g => a.StartsWith(g.Value, StringComparison.Ordinal));
            Assert.NotEqual(a, AuditActions.Display(a));
        });
    }
}
