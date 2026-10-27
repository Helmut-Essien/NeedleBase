using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Orders;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Tests;

/// <summary>Second-audit fixes: shared decimal parser, amount rules, accent-folded search, order-line snapshots.</summary>
public class AuditFixRulesTests
{
    [Theory]
    [InlineData("92.5", 92.5)]
    [InlineData("92,5", 92.5)]
    [InlineData(" 1000 ", 1000)]
    [InlineData("0.125", 0.125)]
    [InlineData("0,125", 0.125)]
    [InlineData("12.25", 12.25)]
    [InlineData("-3", -3)]
    [InlineData(",5", 0.5)]
    public void DecimalInput_Accepts(string text, double expected)
    {
        Assert.True(DecimalInput.TryParse(text, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("1,000")]      // thousands separator, not 1.0
    [InlineData("12,500")]
    [InlineData("1.000")]      // ditto in the other locale (would silently be GH₵ 1)
    [InlineData("1,000.50")]
    [InlineData("1.000,50")]
    [InlineData("1 000")]
    [InlineData("12.")]
    [InlineData("12,")]
    [InlineData(".")]
    [InlineData("abc")]
    [InlineData("1e3")]
    [InlineData("")]
    [InlineData(null)]
    public void DecimalInput_Rejects(string? text) => Assert.False(DecimalInput.TryParse(text, out _));

    [Theory]
    [InlineData(0.01, true)]
    [InlineData(1_000_000, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(1_000_000.01, false)]
    [InlineData(10.005, false)]
    public void UnitPrice_Rules(double price, bool ok) => Assert.Equal(ok, AmountRules.CheckUnitPrice((decimal)price) is null);

    [Theory]
    [InlineData(MeasurementUnit.Centimetres, 300, true)]
    [InlineData(MeasurementUnit.Centimetres, 301, false)]
    [InlineData(MeasurementUnit.Inches, 118, true)]
    [InlineData(MeasurementUnit.Inches, 118.5, false)]
    [InlineData(MeasurementUnit.Inches, 0, false)]
    public void Measurement_Rules(string unit, double value, bool ok)
    {
        Assert.Equal(ok, AmountRules.CheckMeasurement(unit, "chest", (decimal)value) is null);
        Assert.Null(AmountRules.CheckMeasurement(unit, "chest", null));
        var fields = new Dictionary<string, decimal?> { ["chest"] = (decimal)value };
        if (ok) AmountRules.EnsureMeasurements(unit, fields);
        else Assert.Throws<ArgumentException>(() => AmountRules.EnsureMeasurements(unit, fields));
    }

    // ---------------------------------------------------------------- search folding (makes 97b1d85's "accent-insensitive" claim true)

    [Theory]
    [InlineData("Ámà Ɔwusu", "ama", true)]
    [InlineData("Ama Owusu", "ámá", true)]
    [InlineData("Ɛfua Ɔkɔ", "efua oko", true)]
    [InlineData("Abena Owusu-Ansah", "ansah", true)]
    [InlineData("Kofi N'Guessan", "guess", true)]
    [InlineData("Kofi N’Guessan", "guess", true)]
    [InlineData("Abena Owusu-Ansah", "usu", false)]
    public void NameKey_FoldsAccentsAndSplitsOnHyphensAndApostrophes(string name, string term, bool match)
    {
        var key = CustomerSearchKeys.NameKey(name);
        var parsed = CustomerSearchKeys.NameKey(term).Trim();
        Assert.Equal(match, parsed.Split(' ').All(t => key.Contains(" " + t, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("4.5", 4.5)]
    [InlineData("4,5", 4.5)]
    [InlineData("4.125", 4.125)]   // eighths of an inch: three decimals are normal for measurements
    [InlineData("4,375", 4.375)]
    [InlineData("37.250", 37.25)]
    [InlineData("92.500", 92.5)]
    [InlineData("0.0625", 0.0625)]
    [InlineData(" 98 ", 98)]
    [InlineData(",5", 0.5)]
    public void MeasurementInput_Accepts(string text, double expected)
    {
        Assert.True(DecimalInput.TryParseMeasurement(text, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("4.12345")]    // more than 4 decimals
    [InlineData("1,000.5")]
    [InlineData("1.2.3")]
    [InlineData("12.")]
    [InlineData("12,")]
    [InlineData(".")]
    [InlineData("1 000")]
    [InlineData("4 1/8")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void MeasurementInput_Rejects(string? text) => Assert.False(DecimalInput.TryParseMeasurement(text, out _));

    [Fact]
    public void PriceRule_StaysStrict_WhileMeasurementRuleIsNot()
    {
        Assert.False(DecimalInput.TryParse("4.125", out _));
        Assert.False(DecimalInput.TryParse("1.000", out _));
        Assert.True(DecimalInput.TryParseMeasurement("4.125", out var v));
        Assert.Equal("4.125", DecimalInput.Format(v));
    }

    [Fact]
    public void Measurement_DecimalPlaces_AreCapped()
    {
        Assert.Null(AmountRules.CheckMeasurement(MeasurementUnit.Inches, "chest", 4.125m));
        Assert.Null(AmountRules.CheckMeasurement(MeasurementUnit.Inches, "chest", 4.1234m));
        Assert.NotNull(AmountRules.CheckMeasurement(MeasurementUnit.Inches, "chest", 4.12345m));
    }

    [Fact]
    public void WordPrefixMatch_ReturnsRangeInOriginalString_WithCombiningMarksAndEmoji()
    {
        const string decomposed = "Ama\u0301 Owusu"; // "Amá" written with a combining accent
        Assert.Equal((0, 4), CustomerSearchKeys.WordPrefixMatch(decomposed, "ama"));
        Assert.Equal((5, 3), CustomerSearchKeys.WordPrefixMatch(decomposed, "owu"));

        const string emoji = "👑 Ɔkɔ Mensah";
        var (start, length) = CustomerSearchKeys.WordPrefixMatch(emoji, "oko");
        Assert.Equal("Ɔkɔ", emoji.Substring(start, length));
        var men = CustomerSearchKeys.WordPrefixMatch(emoji, "men");
        Assert.Equal("Men", emoji.Substring(men.Start, men.Length));

        Assert.Equal((-1, 0), CustomerSearchKeys.WordPrefixMatch("Abena", "ben"));
        Assert.Equal(-1, CustomerSearchKeys.WordPrefixIndex("Abena", "ben"));
    }

    // ---------------------------------------------------------------- order lines

    [Fact]
    public void OrderLine_WithDeletedProfile_KeepsSnapshot_ButWithoutFieldsIsNotFound()
    {
        var withSnapshot = Line("01MISSINGPROFILE0000000001", new Dictionary<string, decimal?> { ["chest"] = 100m });
        var (lines, error) = OrderLineFactory.Build("biz", "order", [withSnapshot], new Dictionary<string, MeasurementProfile>());
        Assert.Equal(OrderError.None, error);
        var line = Assert.Single(lines!);
        Assert.Null(line.MeasurementProfileId);
        Assert.Contains("chest", line.MeasurementFieldsJson);

        var bare = Line("01MISSINGPROFILE0000000001", new Dictionary<string, decimal?>());
        Assert.Equal(OrderError.MeasurementNotFound, OrderLineFactory.Build("biz", "order", [bare], new Dictionary<string, MeasurementProfile>()).Error);
    }

    [Fact]
    public void OrderLines_TooManyOrBadPrice_AreRejected()
    {
        var many = Enumerable.Range(0, AmountRules.OrderLinesMax + 1).Select(_ => Line(null, null)).ToList();
        Assert.Throws<ArgumentException>(() => OrderLineFactory.Build("biz", "order", many, new Dictionary<string, MeasurementProfile>()));
        var free = Line(null, null);
        free.UnitPrice = 0m;
        Assert.Throws<ArgumentException>(() => OrderLineFactory.Build("biz", "order", [free], new Dictionary<string, MeasurementProfile>()));
    }

    private static CreateOrderLineRequest Line(string? profileId, Dictionary<string, decimal?>? fields) => new()
    {
        GarmentTemplate = GarmentTemplate.Shirt,
        Quantity = 1,
        UnitPrice = 50m,
        MeasurementProfileId = profileId,
        Unit = MeasurementUnit.Centimetres,
        MeasurementFields = fields ?? new Dictionary<string, decimal?>()
    };
}
