using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Tests;

/// <summary>Web token refresh under a page's parallel calls: one refresh, no HttpContext races, no unsafe replays.</summary>
public class TokenRefreshTests
{
    private sealed class FakeAuth(ClaimsPrincipal principal) : IAuthenticationService
    {
        public int SignIns; public int SignOuts; public int Authenticates;
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext c, string? s)
        {
            Interlocked.Increment(ref Authenticates);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, new AuthenticationProperties(), AuthCookieSignIn.CookieScheme)));
        }
        public Task ChallengeAsync(HttpContext c, string? s, AuthenticationProperties? p) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext c, string? s, AuthenticationProperties? p) => Task.CompletedTask;
        public Task SignInAsync(HttpContext c, string? s, ClaimsPrincipal p, AuthenticationProperties? pr) { Interlocked.Increment(ref SignIns); return Task.CompletedTask; }
        public Task SignOutAsync(HttpContext c, string? s, AuthenticationProperties? p) { Interlocked.Increment(ref SignOuts); return Task.CompletedTask; }
    }

    private sealed class Delegate(Func<HttpRequestMessage, Task<HttpResponseMessage>> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => f(r);
    }

    private sealed class Factory(HttpMessageHandler auth) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(auth, false) { BaseAddress = new Uri("http://api.test/") };
    }

    private static (HttpClient Client, FakeAuth Auth, HttpContext Ctx) Make(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> api, Func<HttpRequestMessage, Task<HttpResponseMessage>> refresh)
    {
        var user = "u-" + Guid.NewGuid().ToString("N");
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user), new Claim("AccessToken", "old"), new Claim("RefreshToken", "r1"), new Claim(ClaimTypes.Role, "Owner")],
            AuthCookieSignIn.CookieScheme));
        var auth = new FakeAuth(principal);
        var ctx = new DefaultHttpContext
        {
            User = principal,
            RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(auth).BuildServiceProvider()
        };
        var handler = new TokenRefreshHandler(new HttpContextAccessor { HttpContext = ctx }, new Factory(new Delegate(refresh)), NullLogger<TokenRefreshHandler>.Instance)
        {
            InnerHandler = new Delegate(api)
        };
        return (new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }, auth, ctx);
    }

    private static HttpResponseMessage Status(HttpStatusCode c) => new(c);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Parallel_401s_refresh_once_and_all_retry_with_new_token(int round)
    {
        var refreshes = 0;
        var (client, auth, ctx) = Make(
            r => Task.FromResult(Status(r.Headers.Authorization?.Parameter == "new" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized)),
            async _ =>
            {
                Interlocked.Increment(ref refreshes);
                await Task.Delay(20 * round);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"accessToken":"new","refreshToken":"r2"}""", Encoding.UTF8, "application/json")
                };
            });

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.GetAsync("api/orders")));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, refreshes);
        Assert.Equal("new", ctx.User.FindFirst("AccessToken")!.Value);
        Assert.Equal(0, auth.SignOuts);
    }

    [Fact]
    public async Task Failed_refresh_signs_out_and_returns_401_without_throwing()
    {
        var (client, auth, _) = Make(_ => Task.FromResult(Status(HttpStatusCode.Unauthorized)), _ => Task.FromResult(Status(HttpStatusCode.Unauthorized)));
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.GetAsync("api/orders")));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.True(auth.SignOuts >= 1);
    }

    [Fact]
    public async Task Refresh_outage_keeps_session_and_reports_unavailable()
    {
        var (client, auth, _) = Make(_ => Task.FromResult(Status(HttpStatusCode.Unauthorized)), _ => throw new HttpRequestException("down"));
        using var r = await client.GetAsync("api/orders");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal(0, auth.SignOuts);
    }

    [Fact]
    public async Task Patch_timeout_is_not_replayed()
    {
        var calls = 0;
        var (client, _, _) = Make(_ => { Interlocked.Increment(ref calls); throw new TaskCanceledException("timeout"); }, _ => Task.FromResult(Status(HttpStatusCode.OK)));
        using var r = await client.PatchAsync("api/orders/o1/status", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.GatewayTimeout, r.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Get_timeout_is_not_retried_three_times()
    {
        var calls = 0;
        var (client, _, _) = Make(_ => { Interlocked.Increment(ref calls); throw new TaskCanceledException("timeout"); }, _ => Task.FromResult(Status(HttpStatusCode.OK)));
        using var r = await client.GetAsync("api/orders");
        Assert.Equal(HttpStatusCode.GatewayTimeout, r.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Connection_refused_is_retried_then_unavailable()
    {
        var calls = 0;
        var (client, _, _) = Make(_ =>
        {
            Interlocked.Increment(ref calls);
            throw new HttpRequestException(HttpRequestError.ConnectionError, "refused", new SocketException((int)SocketError.ConnectionRefused));
        }, _ => Task.FromResult(Status(HttpStatusCode.OK)));
        using var r = await client.GetAsync("api/orders");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Refresh_updates_role_from_new_token_without_mutating_cached_identity()
    {
        // header.payload with role=Tailor (unsigned; the web only reads claims for display)
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var jwt = B64("""{"alg":"none","typ":"JWT"}""") + "." + B64("""{"role":"Tailor","exp":4102444800}""") + ".";
        var (client, _, ctx) = Make(
            r => Task.FromResult(Status(r.Headers.Authorization?.Parameter == jwt ? HttpStatusCode.OK : HttpStatusCode.Unauthorized)),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"accessToken":"{{jwt}}","refreshToken":"r2"}""", Encoding.UTF8, "application/json") }));
        var before = ctx.User;
        using var r = await client.GetAsync("api/orders");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Tailor", ctx.User.Role());
        Assert.Equal("Owner", before.Role()); // original identity untouched (siblings may be reading it)
    }
}
