using NeedleBase.Domain.Common;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Domain.Tests;

public class PhoneNumbersTests
{
    [Theory]
    [InlineData("0245550123", "+233245550123")]
    [InlineData("024 555 0123", "+233245550123")]
    [InlineData("024-555-0123", "+233245550123")]
    [InlineData("233245550123", "+233245550123")]
    [InlineData("+233 24 555 0123", "+233245550123")]
    [InlineData("+233 (0)24 555 0123", "+233245550123")]
    [InlineData("00233245550123", "+233245550123")]
    [InlineData("+44 7700 900123", "+447700900123")]
    [InlineData("ask at shop", "ask at shop")]
    [InlineData("  12345  ", "12345")]
    public void Normalize_GhanaToPlus233_OthersKeptSafely(string input, string expected) =>
        Assert.Equal(expected, PhoneNumbers.Normalize(input));

    [Theory]
    [InlineData("0245550123", true)]
    [InlineData("+233245550123", true)]
    [InlineData("+44 7700 900123", true)]
    [InlineData("+233 24 555 012", false)]   // Ghana number one digit short
    [InlineData("024555", false)]
    [InlineData("call me", false)]
    [InlineData("02+45550123", false)]
    [InlineData("", false)]
    public void Check_FormatRule(string input, bool ok) =>
        Assert.Equal(ok, PhoneNumbers.Check(input) is null);

    [Fact]
    public void Customer_StoresNormalisedPhone()
    {
        var c = Customer.Create("c1", "b1", "Ama", "024 555 0123", null, null, DateTimeOffset.UtcNow);
        Assert.Equal("+233245550123", c.Phone);
        Assert.True(PhoneNumbers.Same("0245550123", c.Phone));
    }
}
