using MauiIcons.Material;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Helpers;

namespace NeedleBase.Mobile.Tests;

public class StatusIconGlyphTests
{
    [Fact]
    public void EveryStatusIcon_IsAMaterialFontGlyph_NotTheEnumIndex()
    {
        foreach (var status in OrderStatus.All.Append("FutureStatus"))
        {
            var glyph = OrderStatusPresentation.Glyph(OrderStatusPresentation.Icon(status));
            var cp = Assert.Single(glyph);
            Assert.InRange(cp, '\uE000', '\uF8FF'); // Material Icons live in the Private Use Area
        }

        Assert.Equal("\uE41C", OrderStatusPresentation.Glyph(MaterialIcons.Straighten)); // Fitting (was U+076E)
        Assert.Equal("\uE8B5", OrderStatusPresentation.Glyph(MaterialIcons.Schedule));   // Overdue
    }

    [Fact]
    public void NoXamlOrCode_UsesTheUnregisteredMaterialFontName()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.sln*").Any())
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var mobile = Path.Combine(dir!.FullName, "src", "NeedleBase.Mobile");
        var offenders = Directory.EnumerateFiles(mobile, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".xaml") || f.EndsWith(".cs")) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("FontFamily=\"MaterialIcons-Regular\""))
            .ToList();
        Assert.Empty(offenders); // the alias is "MaterialIcons" (OrderStatusPresentation.MaterialFontFamily)
    }

    /// <summary>Codepoints checked by rendering each one from the bundled font (new, scissors, hanger, ruler, ...).</summary>
    [Theory]
    [InlineData(OrderStatus.New, 0xE05E)]
    [InlineData(OrderStatus.Cutting, 0xE14E)]
    [InlineData(OrderStatus.Sewing, 0xF19E)]
    [InlineData(OrderStatus.Fitting, 0xE41C)]
    [InlineData(OrderStatus.Ready, 0xE86C)]
    [InlineData(OrderStatus.Collected, 0xE876)]
    [InlineData(OrderStatus.Cancelled, 0xE5C9)]
    [InlineData("FutureStatus", 0xE88E)]
    public void EachStatus_MapsToItsVerifiedGlyph(string status, int codepoint) =>
        Assert.Equal(((char)codepoint).ToString(), OrderStatusPresentation.Glyph(OrderStatusPresentation.Icon(status)));

    [Fact]
    public void EveryStatusGlyph_ExistsInTheBundledMaterialFont()
    {
        var asm = typeof(MaterialIcons).Assembly;
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var mapped = FormatFourCodepoints(ms.ToArray());

        var glyphs = OrderStatus.All.Append("FutureStatus")
            .Select(s => OrderStatusPresentation.Glyph(OrderStatusPresentation.Icon(s)))
            .Append(OrderStatusPresentation.Glyph(MaterialIcons.Schedule));
        Assert.All(glyphs, g => Assert.Contains((int)g[0], mapped));
        Assert.Equal("MaterialIcons", OrderStatusPresentation.MaterialFontFamily); // alias UseMaterialMauiIcons registers
    }

    /// <summary>Codepoints covered by the font's cmap format-4 segments (enough for the BMP icon range).</summary>
    private static HashSet<int> FormatFourCodepoints(byte[] d)
    {
        static int U16(byte[] b, int o) => (b[o] << 8) | b[o + 1];
        static int U32(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        var cmap = 0;
        for (var i = 0; i < U16(d, 4); i++)
        {
            if (System.Text.Encoding.ASCII.GetString(d, 12 + 16 * i, 4) == "cmap")
            {
                cmap = U32(d, 20 + 16 * i);
            }
        }

        var result = new HashSet<int>();
        for (var i = 0; i < U16(d, cmap + 2); i++)
        {
            var o = cmap + U32(d, cmap + 8 + 8 * i);
            if (U16(d, o) != 4)
            {
                continue;
            }

            var seg = U16(d, o + 6) / 2;
            for (var k = 0; k < seg; k++)
            {
                var end = U16(d, o + 14 + 2 * k);
                var start = U16(d, o + 16 + 2 * seg + 2 * k);
                for (var cp = start; cp <= end; cp++)
                {
                    result.Add(cp);
                }
            }
        }

        return result;
    }
}
