using NeedleBase.Mobile.Configuration;

namespace NeedleBase.Mobile.Tests;

/// <summary>G10: Release API URL comes from build config; default is a clearly-marked placeholder.</summary>
public class ApiEndpointTests
{
    [Fact]
    public void ReleaseBaseUrl_DefaultBuild_IsPlaceholder()
    {
        var url = ApiEndpoint.ReleaseBaseUrl();
        Assert.NotNull(url);
        Assert.True(ApiEndpoint.IsPlaceholder(url));
    }

    [Theory]
    [InlineData("https://api.example.com", "https://api.example.com/")]
    [InlineData("https://api.example.com/v1/", "https://api.example.com/v1/")]
    [InlineData("http://api.example.com/", null)]
    [InlineData("not a url", null)]
    [InlineData("", null)]
    public void Normalize_RequiresHttpsAndTrailingSlash(string input, string? expected)
    {
        Assert.Equal(expected, ApiEndpoint.Normalize(input));
    }
}
