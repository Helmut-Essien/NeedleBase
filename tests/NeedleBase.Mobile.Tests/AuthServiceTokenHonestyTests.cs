using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.Tests;

public class AuthServiceTokenHonestyTests
{
    [Fact]
    public void TokenRefreshResult_Transient_DoesNotMarkInvalidSession()
    {
        var result = TokenRefreshResult.Transient("offline");
        Assert.True(result.IsTransientFailure);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void TokenRefreshResult_InvalidSession_IsNotTransient()
    {
        var result = TokenRefreshResult.InvalidSession("401");
        Assert.False(result.IsTransientFailure);
        Assert.False(result.Succeeded);
    }
}
