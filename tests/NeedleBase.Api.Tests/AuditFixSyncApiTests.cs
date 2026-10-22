using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Application.Sync;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Tests;

/// <summary>
/// Second-audit fixes on the sync/auth API: refresh grace window, sync epoch, per-user rate limit.
/// In the sync test class so epoch bumps never run beside other pulls.
/// </summary>
public partial class SyncEndpointTests
{
    // ---------------------------------------------------------------- refresh-token grace window

    [Fact]
    public async Task Refresh_LostResponseRetry_WorksOnce_ThenReuseRevokesFamily()
    {
        var (_, t0) = await RegisterWithRefreshAsync("Grace");
        var t1 = await RefreshOkAsync(t0);

        var retried = await RefreshOkAsync(t0);           // response for t1 was "lost": grace
        Assert.NotEqual(t1, retried);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(t0)).Status);       // second grace use = reuse
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(retried)).Status);  // whole family revoked
    }

    [Fact]
    public async Task Refresh_GraceDenied_WhenSuccessorAlreadyUsed()
    {
        var (_, t0) = await RegisterWithRefreshAsync("GraceUsed");
        var t1 = await RefreshOkAsync(t0);
        var t2 = await RefreshOkAsync(t1);                // device moved on

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(t0)).Status); // replay of t0 = theft
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(t2)).Status);
    }

    [Fact]
    public async Task Refresh_GraceDenied_AfterWindow()
    {
        var (userId, t0) = await RegisterWithRefreshAsync("GraceLate");
        var t1 = await RefreshOkAsync(t0);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var old = DateTime.UtcNow - NeedleBase.Infrastructure.Identity.AuthService.RefreshReuseGrace - TimeSpan.FromSeconds(5);
            await db.RefreshTokens.Where(t => t.ApplicationUserId == userId && t.RevokedUtc != null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedUtc, old));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(t0)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(t1)).Status);
    }

    // ---------------------------------------------------------------- sync epoch

    [Fact]
    public async Task EpochBump_OldCursorGets400WithReset_FreshPullWorks_NoLoop()
    {
        var token = await RegisterAsync("Epoch");
        var customer = (await PushAsync(token, CustomerUpsert(Customer("Before Restore"), null))).Customer!;
        var cursor = (await PullAllAsync(token)).Cursor;

        await SetHighWaterAsync(long.MaxValue / 2);       // simulate ids going backwards (restore to a new cluster)

        // The re-stamp runs in the background: meanwhile sync answers 503 SYNC_MAINTENANCE with Retry-After.
        await AssertMaintenanceThenWaitAsync(token, cursor);

        var (status, code, reset) = await PullRawAsync(token, cursor);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(SyncErrorCodes.EpochChanged, code);
        Assert.Equal(SyncErrorCodes.CursorInvalid, reset);   // older apps key off this and still reset

        var fresh = await PullAllAsync(token);
        Assert.Contains(fresh.Customers, c => c.Id == customer.Id);
        var epoch = await EpochAsync();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await PullRawAsync(token, fresh.Cursor)).Status);
        }

        Assert.Equal(epoch, await EpochAsync());
    }

    [Fact]
    public async Task CursorAheadOfCap_SameEpoch_BumpsOnce_OldEpochCursorDoesNotBumpAgain()
    {
        var token = await RegisterAsync("Ahead");
        await PushAsync(token, CustomerUpsert(Customer("Ahead"), null));
        var cursor = (await PullAllAsync(token)).Cursor;
        Assert.True(SyncCursor.TryDecode(cursor, out var decoded));
        decoded.Customers = new SyncPosition(decoded.HighestPosition + 1_000_000_000, "ZZZZ");
        var ahead = decoded.Encode();                     // a restore that also rolled SyncState back
        var before = await EpochAsync();

        await AssertMaintenanceThenWaitAsync(token, ahead);
        // A re-stamp that lost a lock race with other tests' writes is retried by the next pull (as on a real
        // server), so let a pull settle it before counting: exactly one bump either way.
        Assert.Equal(SyncErrorCodes.EpochChanged, (await PullRawAsync(token, ahead)).Code);
        Assert.Equal(before + 1, await EpochAsync());
        Assert.Equal(SyncErrorCodes.EpochChanged, (await PullRawAsync(token, ahead)).Code);
        Assert.Equal(before + 1, await EpochAsync());     // no bump storm from old-epoch cursors
    }

    // ---------------------------------------------------------------- rate limit

    [Fact]
    public async Task RateLimit_FirstPullOf60Pages_IsFine_BurstBeyondLimitGets429WithRetryAfter()
    {
        var token = await RegisterAsync("Rate");
        for (var i = 0; i < 60; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await PullRawAsync(token, null)).Status);
        }

        HttpResponseMessage? limited = null;
        for (var i = 0; i < 400 && limited is null; i++)
        {
            using var request = Authorized(HttpMethod.Post, "/api/sync/pull", token, new SyncPullRequest { Limit = 1 });
            var response = await _client.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) limited = response;
            else response.Dispose();
        }

        Assert.NotNull(limited);
        Assert.NotNull(limited!.Headers.RetryAfter?.Delta);
        limited.Dispose();

        var other = await RegisterAsync("RateOther");     // per user, not global
        Assert.Equal(HttpStatusCode.OK, (await PullRawAsync(other, null)).Status);
    }

    // ================================================================ helpers

    private async Task AssertMaintenanceThenWaitAsync(string token, string? cursor)
    {
        using (var request = Authorized(HttpMethod.Post, "/api/sync/pull", token, new SyncPullRequest { Cursor = cursor, Limit = 200 }))
        using (var response = await _client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.NotNull(response.Headers.RetryAfter?.Delta);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(SyncErrorCodes.Maintenance, doc.RootElement.GetProperty("code").GetString());
        }

        await ApplicationDbContext.WaitForEpochBumpAsync().WaitAsync(TimeSpan.FromSeconds(60));
    }

    /// <summary>One pull; a 503 SYNC_MAINTENANCE (a background re-stamp, possibly a no-op re-check) is waited out like a device would.</summary>
    private async Task<(HttpStatusCode Status, string? Code, string? Reset)> PullRawAsync(string token, string? cursor)
    {
        for (var attempt = 0; ; attempt++)
        {
            var result = await PullRawOnceAsync(token, cursor);
            if (result.Status != HttpStatusCode.ServiceUnavailable || attempt >= 50)
            {
                return result;
            }

            await ApplicationDbContext.WaitForEpochBumpAsync().WaitAsync(TimeSpan.FromSeconds(60));
            await Task.Delay(50);
        }
    }

    private async Task<(HttpStatusCode Status, string? Code, string? Reset)> PullRawOnceAsync(string token, string? cursor)
    {
        using var request = Authorized(HttpMethod.Post, "/api/sync/pull", token, new SyncPullRequest { Cursor = cursor, Limit = 200 });
        using var response = await _client.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            return (response.StatusCode, null, null);
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string? Get(string name) => doc.RootElement.TryGetProperty(name, out var v) ? v.GetString() : null;
        return (response.StatusCode, Get("code"), Get("reset"));
    }

    private async Task SetHighWaterAsync(long value)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SyncState\" SET \"HighWaterTx\" = {value} WHERE \"Id\" = 1");
    }

    private async Task<long> EpochAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().GetSyncEpochAsync();
    }

    private async Task<(string UserId, string RefreshToken)> RegisterWithRefreshAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var register = new RegisterBusinessRequest(
            $"grace{label}{suffix}@ama-stitches.test", "Passw0rd!", "Ama", "Owusu", $"Grace Biz {label} {suffix}", "+233200000000");
        using var response = await _client.PostAsJsonAsync("/api/auth/register-business", register, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (doc.RootElement.GetProperty("user").GetProperty("userId").GetString()!, doc.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private async Task<(HttpStatusCode Status, string? RefreshToken)> RefreshAsync(string refreshToken)
    {
        using var response = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest(string.Empty, refreshToken), JsonOptions);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return (response.StatusCode, null);
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, doc.RootElement.GetProperty("refreshToken").GetString());
    }

    private async Task<string> RefreshOkAsync(string refreshToken)
    {
        var (status, next) = await RefreshAsync(refreshToken);
        Assert.Equal(HttpStatusCode.OK, status);
        return next!;
    }
}
