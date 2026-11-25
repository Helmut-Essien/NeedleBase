using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Mobile.Configuration;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.Tests;

/// <summary>Free→online upgrade: resume without re-registering; pending detection.</summary>
[Collection("LocalFreeModeGate")]
public class FreeUpgradeAuthTests
{
    [Fact]
    public async Task UpgradeFromFree_WhenPendingAndTokens_CompletesWithoutRegisterCall()
    {
        var storage = new MemoryStorage();
        await storage.SetAsync(AuthService.FreeModeKey, "1");
        await storage.SetAsync(AuthService.BusinessIdKey, "01FREEFROM0000000000000001");
        await storage.SetAsync(AuthService.BusinessNameKey, "Ama Stitches");
        await storage.SetAsync(AuthService.UserIdKey, "01FREEUSER0000000000000001");
        await storage.SetAsync(AuthService.EmailKey, string.Empty);
        await storage.SetAsync(AuthService.FirstNameKey, "Owner");
        await storage.SetAsync(AuthService.SurnameKey, string.Empty);
        await storage.SetAsync(AuthService.RoleKey, "Owner");
        await storage.SetAsync(AuthService.AccessTokenKey, "access");
        await storage.SetAsync(AuthService.RefreshTokenKey, "refresh");

        var onlineUser = new AuthUserDto(
            "01ONLINEUSER00000000000001",
            "owner@shop.test",
            "Ama",
            "Mensah",
            "01ONLINETO0000000000000001",
            "Ama Stitches",
            "Owner");
        var pending = JsonSerializer.Serialize(
            new { FromBusinessId = "01FREEFROM0000000000000001", User = onlineUser },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await storage.SetAsync(AuthService.FreeUpgradePendingKey, pending);

        var handler = new CountingHandler();
        var upgrade = new RecordingFreeUpgrade();
        var auth = new AuthService(
            new StubFactory(handler),
            storage,
            new NoopDatabase(),
            new NoopFreeModeLocalService(),
            upgrade,
            Options.Create(new ApiSettings()));

        var result = await auth.UpgradeFromFreeAsync(new RegisterBusinessRequest(
            "owner@shop.test",
            "Password1!",
            "Ama",
            "Mensah",
            "Ama Stitches",
            null,
            "LICENCE-KEY"));

        Assert.NotNull(result);
        Assert.Equal(onlineUser.BusinessId, result!.BusinessId);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(1, upgrade.RemapCalls);
        Assert.Null(await storage.GetAsync(AuthService.FreeUpgradePendingKey));
        Assert.False(await auth.IsFreeModeAsync());
        Assert.Equal(onlineUser.BusinessId, await auth.GetBusinessIdAsync());
    }

    [Fact]
    public async Task HasPendingFreeUpgrade_TrueWhenBlobPresent()
    {
        var storage = new MemoryStorage();
        await storage.SetAsync(AuthService.FreeUpgradePendingKey, "{}");
        var auth = new AuthService(
            new StubFactory(new CountingHandler()),
            storage,
            new NoopDatabase(),
            new NoopFreeModeLocalService(),
            new NoopFreeUpgradeLocalService(),
            Options.Create(new ApiSettings()));

        // Malformed JSON is cleared — treat as not pending.
        Assert.False(await auth.HasPendingFreeUpgradeAsync());

        var user = new AuthUserDto("U", "a@b.c", "A", "B", "B1", "Biz", "Owner");
        var json = JsonSerializer.Serialize(
            new { FromBusinessId = "F1", User = user },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await storage.SetAsync(AuthService.FreeUpgradePendingKey, json);
        Assert.True(await auth.HasPendingFreeUpgradeAsync());
    }

    [Fact]
    public async Task TryResumeFreeUpgrade_WithoutTokens_ClearsPending()
    {
        var storage = new MemoryStorage();
        var user = new AuthUserDto("U", "a@b.c", "A", "B", "B1", "Biz", "Owner");
        var json = JsonSerializer.Serialize(
            new { FromBusinessId = "F1", User = user },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await storage.SetAsync(AuthService.FreeUpgradePendingKey, json);

        var auth = new AuthService(
            new StubFactory(new CountingHandler()),
            storage,
            new NoopDatabase(),
            new NoopFreeModeLocalService(),
            new NoopFreeUpgradeLocalService(),
            Options.Create(new ApiSettings()));

        Assert.False(await auth.TryResumeFreeUpgradeAsync());
        Assert.Null(await storage.GetAsync(AuthService.FreeUpgradePendingKey));
    }

    private sealed class RecordingFreeUpgrade : IFreeUpgradeLocalService
    {
        public int RemapCalls;

        public Task<bool> HasShopRowsAsync(string businessId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task RemapBusinessIdAsync(
            string fromBusinessId, string toBusinessId, CancellationToken cancellationToken = default)
        {
            RemapCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost/") };
    }

    private sealed class MemoryStorage : ISecureStorageService
    {
        private readonly Dictionary<string, string> _values = new();

        public Task SetAsync(string key, string value)
        {
            lock (_values)
            {
                _values[key] = value;
            }

            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(string key)
        {
            lock (_values)
            {
                return Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);
            }
        }

        public void Remove(string key)
        {
            lock (_values)
            {
                _values.Remove(key);
            }
        }
    }

    private sealed class NoopDatabase : ILocalDatabaseService
    {
        public Task MigrateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> WipeAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<int> CountUnsyncedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
