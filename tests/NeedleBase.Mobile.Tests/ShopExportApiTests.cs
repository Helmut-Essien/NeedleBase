using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.Tests;

public sealed class ShopExportApiTests
{
    [Theory]
    [InlineData("needlebase-shop-20261125.json", "needlebase-shop-20261125.json")]
    [InlineData("../etc/passwd", "passwd")]
    [InlineData("..\\windows\\system32\\x.json", "x.json")]
    [InlineData("exports/nested/shop.json", "shop.json")]
    [InlineData(".", null)]
    [InlineData("..", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void SafeFileName_UsesBasenameOnly(string? input, string? expected) =>
        Assert.Equal(expected, ShopExportApi.SafeFileName(input));
}
