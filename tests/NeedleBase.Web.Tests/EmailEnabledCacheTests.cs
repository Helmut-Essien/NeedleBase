using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Tests;

[Collection("EmailEnabledCache")]
public sealed class EmailEnabledCacheTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public TimeSpan Delay;
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(Delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"emailEnabled\":true}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri("http://api.example.test/") };
    }

    [Fact]
    public async Task StaleAnswer_IsServedAtOnce_WhileOneBackgroundRefreshRuns()
    {
        ApiAuthService.ResetEmailEnabledCache();
        var handler = new Handler();
        var service = new ApiAuthService(new Factory(handler), NullLogger<ApiAuthService>.Instance);
        Assert.True(await service.EmailEnabledAsync());

        ApiAuthService.ExpireEmailEnabledCache();
        handler.Delay = TimeSpan.FromSeconds(1.5); // slow Api
        var sw = Stopwatch.StartNew();
        var answers = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.EmailEnabledAsync()));
        sw.Stop();

        // Before: each page render after expiry waited for the Api (up to 3 s), and every concurrent render called it.
        Assert.All(answers, Assert.True);
        Assert.True(sw.ElapsedMilliseconds < 200, $"took {sw.ElapsedMilliseconds} ms");
        await Task.Delay(100);
        Assert.Equal(2, handler.Calls); // first fill + a single refresh
        ApiAuthService.ResetEmailEnabledCache();
    }
}
