using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Api.Configuration;

namespace NeedleBase.Api.Tests;

/// <summary>The Api as the IIS sub-application /api of the Web site (single-site hosting, docs/deployment.md).</summary>
public class ApiPathBaseTests : IClassFixture<ApiPathBaseTests.SubApplicationFactory>
{
    /// <summary>Does what IIS integration does in a sub-application: "/api" moves from Path into PathBase.</summary>
    public sealed class SubApplicationFactory : NeedleBaseApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, IisSubApplicationFilter>());
        }

        private sealed class IisSubApplicationFilter : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase,
                            out var matched, out var remaining))
                    {
                        context.Request.PathBase = matched;
                        context.Request.Path = remaining;
                    }
                    return nextMiddleware(context);
                });
                next(app);
            };
        }
    }

    private readonly HttpClient _client;

    public ApiPathBaseTests(SubApplicationFactory factory) => _client = factory.CreateClient();

    [Theory]
    [InlineData("/api", "/orders", "", "/api/orders")]
    [InlineData("/API", "/orders", "", "/api/orders")]
    [InlineData("/api", "", "", "/api")]
    [InlineData("", "/api/orders", "", "/api/orders")]
    [InlineData("/other", "/api/orders", "/other", "/api/orders")]
    [InlineData("/api/v2", "/orders", "/api/v2", "/orders")]
    public void Restore_only_folds_back_the_api_sub_application(string pathBase, string path, string expectedBase, string expectedPath)
    {
        var request = new DefaultHttpContext().Request;
        request.PathBase = pathBase;
        request.Path = path;

        ApiPathBase.Restore(request);

        Assert.Equal(expectedBase, request.PathBase.Value ?? "");
        Assert.Equal(expectedPath, request.Path.Value);
    }

    [Theory]
    [InlineData("/api/health/live")]
    [InlineData("/api/health/ready")]
    [InlineData("/api/test/ping")]
    public async Task Endpoints_answer_under_the_sub_application(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Protected_route_is_found_under_the_sub_application()
    {
        // 401 (route found, sign-in needed), not 404 (route lost to the PathBase).
        var response = await _client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/api/api/test/ping")] // /api is added back once, never twice
    public async Task Unknown_route_is_still_404(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
