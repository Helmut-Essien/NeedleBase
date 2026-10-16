using NeedleBase.Application.Common;
using NUlid;

namespace NeedleBase.Application.Tests;

public class IdGeneratorTests
{
    [Fact]
    public void NewId_ReturnsParseableUlidString()
    {
        var id = IdGenerator.NewId();

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.True(Ulid.TryParse(id, out _));
    }

    [Fact]
    public void NewId_ProducesUniqueValues()
    {
        var a = IdGenerator.NewId();
        var b = IdGenerator.NewId();
        Assert.NotEqual(a, b);
    }
}
