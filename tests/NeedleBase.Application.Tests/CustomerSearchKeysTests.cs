using NeedleBase.Application.Common;

namespace NeedleBase.Application.Tests;

public class CustomerSearchKeysTests
{
    [Theory]
    [InlineData("+233 24 555 0123", "245550123")]
    [InlineData("024 555 0123", "245550123")]
    [InlineData("0245550123", "245550123")]
    [InlineData("233245550123", "245550123")]
    [InlineData("", "")]
    public void PhoneDigits_NormalisesGhanaPrefixes(string phone, string expected) =>
        Assert.Equal(expected, CustomerSearchKeys.PhoneDigits(phone));

    [Theory]
    [InlineData("024", null, "24")]
    [InlineData("24 123", null, "24123")]
    [InlineData("+23324", null, "24")]
    [InlineData("2", null, null)]            // one digit is too broad
    [InlineData("  Efua  MEN ", "efua men", null)]
    [InlineData("ef%u_a", "efua", null)]     // LIKE wildcards stripped
    [InlineData("   ", null, null)]
    public void Parse_SplitsNameAndPhone(string input, string? name, string? phone)
    {
        var query = CustomerSearchKeys.Parse(input);
        Assert.Equal(name, query.NameTerm);
        Assert.Equal(phone, query.PhoneTerm);
    }

    [Fact]
    public void NameKey_IsLowerCollapsedWithLeadingSpace() =>
        Assert.Equal(" efua mensah", CustomerSearchKeys.NameKey("  Efua   Mensah "));

    [Theory]
    [InlineData("+233245550123", "+233 24 555 0123")]
    [InlineData("0245550123", "+233 24 555 0123")]
    [InlineData("+44 20 7946 0958", "+44 20 7946 0958")]
    public void FormatPhone_ShowsGhanaNumbersWithCountryCode(string phone, string expected) =>
        Assert.Equal(expected, CustomerSearchKeys.FormatPhone(phone));

    [Theory]
    [InlineData("Efua Mensah", "men", 5)]
    [InlineData("Efua Mensah", "EF", 0)]
    [InlineData("Efua Mensah", "fua", -1)]
    public void WordPrefixIndex_FindsHighlightStart(string name, string term, int expected) =>
        Assert.Equal(expected, CustomerSearchKeys.WordPrefixIndex(name, term));
}
