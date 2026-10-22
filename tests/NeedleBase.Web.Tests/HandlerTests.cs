using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NeedleBase.Web.Handlers;

namespace NeedleBase.Web.Tests;

public class UnauthorizedRedirectHandlerTests
{
    private sealed class Fixed(HttpStatusCode code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => Task.FromResult(new HttpResponseMessage(code));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Passes_every_status_through_for_the_page_to_handle(HttpStatusCode code)
    {
        using var client = new HttpClient(new UnauthorizedRedirectHandler(NullLogger<UnauthorizedRedirectHandler>.Instance) { InnerHandler = new Fixed(code) });
        using var response = await client.GetAsync("http://api.test/x");
        Assert.Equal(code, response.StatusCode);
    }
}
