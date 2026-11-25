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
    private static readonly JsonSerializerOptions Camel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
        await WritePendingAsync(
            storage,
            "01FREEFROM0000000000000001",
            OnlineUser(),
            "access",
            "refresh");

        var handler = new CountingHandler();
        var upgrade = new RecordingFreeUpgrade();
        var auth = NewAuth(storage, handler, upgrade);

        var result = await auth.UpgradeFromFreeAsync(SampleRequest());

        Assert.NotNull(result);
        Assert.Equal(OnlineUser().BusinessId, result!.BusinessId);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(1, upgrade.RemapCalls);
        Assert.Null(await storage.GetAsync(AuthService.FreeUpgradePendingKey));
        Assert.False(await auth.IsFreeModeAsync());
        Assert.Equal(OnlineUser().BusinessId, await auth.GetBusinessIdAsync());
    }

    [Fact]
    public async Task UpgradeFromFree_WhenFreeFlagClearedButPending_StillCompletes()
    {
        // CompleteFreeUpgrade clears FreeModeKey after remap; a later storage failure must still resume.
        var storage = new MemoryStorage();
        await storage.SetAsync(AuthService.BusinessIdKey, "01FREEFROM0000000000000001");
        await storage.SetAsync(AuthService.AccessTokenKey, "access");
        await storage.SetAsync(AuthService.RefreshTokenKey, "refresh");
        await WritePendingAsync(
            storage,
            "01FREEFROM0000000000000001",
            OnlineUser(),
            "access",
            "refresh");

        var handler = new CountingHandler();
        var upgrade = new RecordingFreeUpgrade();
        var auth = NewAuth(storage, handler, upgrade);

        Assert.False(await auth.IsFreeModeAsync());
        var result = await auth.UpgradeFromFreeAsync(SampleRequest());

        Assert.NotNull(result);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(1, upgrade.RemapCalls);
        Assert.Null(await storage.GetAsync(AuthService.FreeUpgradePendingKey));
    }

    [Fact]
    public async Task TryResume_RestoresTokensFromPending_WhenKeysMissing()
    {
        var storage = new MemoryStorage();
        await WritePendingAsync(
            storage,
            "01FREEFROM0000000000000001",
            OnlineUser(),
            "pending-access",
            "pending-refresh");

        var upgrade = new RecordingFreeUpgrade();
        var auth = NewAuth(storage, new CountingHandler(), upgrade);

        Assert.True(await auth.TryResumeFreeUpgradeAsync());
        Assert.Equal("pending-access", await storage.GetAsync(AuthService.AccessTokenKey));
        Assert.Equal("pending-refresh", await storage.GetAsync(AuthService.RefreshTokenKey));
        Assert.Equal(1, upgrade.RemapCalls);
        Assert.Null(await storage.GetAsync(AuthService.FreeUpgradePendingKey));
    }

    [Fact]
    public async Task HasPendingFreeUpgrade_TrueWhenBlobPresent()
    {
        var storage = new MemoryStorage();
        await storage.SetAsync(AuthService.FreeUpgradePendingKey, "{}");
        var auth = NewAuth(storage, new CountingHandler(), new NoopFreeUpgradeLocalService());

        // Incomplete JSON is cleared — treat as not pending.
        Assert.False(await auth.HasPendingFreeUpgradeAsync());

        await WritePendingAsync(storage, "F1", OnlineUser(), "a", "r");
        Assert.True(await auth.HasPendingFreeUpgradeAsync());
    }

    [Fact]
    public async Task TryResumeFreeUpgrade_WithoutTokensOrPendingSecrets_LeavesPending()
    {
        // Legacy blob without tokens cannot resume; keep nothing useful (cleared as invalid).
        var storage = new MemoryStorage();
        var user = OnlineUser();
        var json = JsonSerializer.Serialize(new { FromBusinessId = "F1", User = user }, Camel);
        await storage.SetAsync(AuthService.FreeUpgradePendingKey, json);

        var auth = NewAuth(storage, new CountingHandler(), new NoopFreeUpgradeLocalService());

        Assert.False(await auth.TryResumeFreeUpgradeAsync());
        Assert.Null(await storage.GetAsync(AuthService.FreeUpgradePendingKey));
    }

    [Fact]
    public async Task UpgradeFromFree_RegisterFailed_SignsInAndRemapsWithoutWipe()
    {
        var storage = new MemoryStorage();
        await storage.SetAsync(AuthService.FreeModeKey, "1");
        await storage.SetAsync(AuthService.BusinessIdKey, "01FREEFROM0000000000000001");
        await storage.SetAsync(AuthService.BusinessNameKey, "Ama Stitches");
        await storage.SetAsync(AuthService.UserIdKey, "01FREEUSER0000000000000001");
        await storage.SetAsync(AuthService.FirstNameKey, "Owner");
        await storage.SetAsync(AuthService.SurnameKey, string.Empty);
        await storage.SetAsync(AuthService.RoleKey, "Owner");
        await storage.SetAsync(AuthService.EmailKey, string.Empty);

        var loginJson = JsonSerializer.Serialize(
            new
            {
                accessToken = "login-access",
                refreshToken = "login-refresh",
                user = OnlineUser()
            },
            Camel);
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """{"code":"REGISTER_FAILED","title":"Unable to register"}""",
                    Encoding.UTF8,
                    "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(loginJson, Encoding.UTF8, "application/json")
            });
        var upgrade = new RecordingFreeUpgrade();
        var database = new NoopDatabase();
        var auth = new AuthService(
            new StubFactory(handler),
            storage,
            database,
            new NoopFreeModeLocalService(),
            upgrade,
            Options.Create(new ApiSettings()));

        var result = await auth.UpgradeFromFreeAsync(SampleRequest());

        Assert.NotNull(result);
        Assert.Equal(OnlineUser().BusinessId, result!.BusinessId);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(1, upgrade.RemapCalls);
        Assert.Equal(0, database.Wipes);
        Assert.False(await auth.IsFreeModeAsync());
        Assert.Null(await storage.GetAsync(AuthService.FreeUpgradeIntentKey));
        Assert.Null(await storage.GetAsync(AuthService.FreeUpgradePendingKey));
    }

    private static AuthUserDto OnlineUser() => new(
        "01ONLINEUSER00000000000001",
        "owner@shop.test",
        "Ama",
        "Mensah",
        "01ONLINETO0000000000000001",
        "Ama Stitches",
        "Owner");

    private static RegisterBusinessRequest SampleRequest() => new(
        "owner@shop.test",
        "Password1!",
        "Ama",
        "Mensah",
        "Ama Stitches",
        null,
        "LICENCE-KEY");

    private static async Task WritePendingAsync(
        MemoryStorage storage,
        string fromBusinessId,
        AuthUserDto user,
        string access,
        string refresh)
    {
        var json = JsonSerializer.Serialize(
            new
            {
                FromBusinessId = fromBusinessId,
                User = user,
                AccessToken = access,
                RefreshToken = refresh
            },
            Camel);
        await storage.SetAsync(AuthService.FreeUpgradePendingKey, json);
    }

    [Fact]
    public async Task Login_WithFreeUpgradeIntent_RemapsWithoutWipe()
    {
        var storage = new MemoryStorage();
        await storage.SetAsync(AuthService.FreeModeKey, "1");
        await storage.SetAsync(AuthService.BusinessIdKey, "01FREEFROM0000000000000001");
        await storage.SetAsync(AuthService.FreeUpgradeIntentKey, "01FREEFROM0000000000000001");
        await storage.SetAsync(AuthService.UserIdKey, "01FREEUSER0000000000000001");
        await storage.SetAsync(AuthService.BusinessNameKey, "Ama Stitches");
        await storage.SetAsync(AuthService.FirstNameKey, "Owner");
        await storage.SetAsync(AuthService.SurnameKey, string.Empty);
        await storage.SetAsync(AuthService.RoleKey, "Owner");
        await storage.SetAsync(AuthService.EmailKey, string.Empty);

        var loginJson = JsonSerializer.Serialize(
            new
            {
                accessToken = "login-access",
                refreshToken = "login-refresh",
                user = OnlineUser()
            },
            Camel);
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(loginJson, Encoding.UTF8, "application/json")
            });
        var upgrade = new RecordingFreeUpgrade();
        var database = new NoopDatabase();
        var auth = new AuthService(
            new StubFactory(handler),
            storage,
            database,
            new NoopFreeModeLocalService(),
            upgrade,
            Options.Create(new ApiSettings()));

        var result = await auth.LoginAsync(new LoginRequest("owner@shop.test", "Password1!"));

        Assert.NotNull(result);
        Assert.Equal(1, upgrade.RemapCalls);
        Assert.Equal(0, database.Wipes);
        Assert.False(await auth.IsFreeModeAsync());
        Assert.Null(await storage.GetAsync(AuthService.FreeUpgradeIntentKey));
    }

    private static AuthService NewAuth(
        MemoryStorage storage, HttpMessageHandler handler, IFreeUpgradeLocalService upgrade) =>
        new(
            new StubFactory(handler),
            storage,
            new NoopDatabase(),
            new NoopFreeModeLocalService(),
            upgrade,
            Options.Create(new ApiSettings()));

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

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int _index;
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            var i = Interlocked.Increment(ref _index) - 1;
            var response = i < responses.Length
                ? responses[i]
                : new HttpResponseMessage(HttpStatusCode.InternalServerError);
            return Task.FromResult(response);
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
        public int Wipes;

        public Task MigrateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> WipeAsync(CancellationToken cancellationToken = default)
        {
            Wipes++;
            return Task.FromResult(true);
        }

        public Task<int> CountUnsyncedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
