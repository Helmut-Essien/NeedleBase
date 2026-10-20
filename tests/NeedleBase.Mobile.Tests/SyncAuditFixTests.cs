using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Configuration;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

/// <summary>Regression tests for the second edge-case audit fixes (and the re-audit of those fixes).</summary>
public partial class SyncEdgeCaseTests
{
    // ---------------------------------------------------------------- 1 rows from pre-sync builds are adopted

    [Fact]
    public async Task Adoption_NeverSyncedDevice_QueuesLegacyRowsOnce_AndPushesParentsFirst()
    {
        var server = new FakeServer();
        await using var device = await DeviceAsync();
        var (customerId, measurementId, orderId) = await AddLegacyRowsAsync(device, "Legacy");

        var engine = Engine(device, server);
        await engine.RunAsync(Biz);

        Assert.True(server.Customers.ContainsKey(customerId));
        Assert.True(server.Measurements.ContainsKey(measurementId));
        Assert.True(server.Orders.ContainsKey(orderId));
        Assert.Equal([SyncEntityType.Customer, SyncEntityType.Measurement, SyncEntityType.Order], server.PushLog);
        Assert.Empty(await QueueAsync(device));

        // Re-audit: no double queueing on later runs.
        await engine.RunAsync(Biz);
        await engine.RunAsync(Biz);
        Assert.Equal(3, server.PushLog.Count);
    }

    [Fact]
    public async Task Adoption_SyncedDevice_ResyncKeepsUnseenLegacyRow_AndDropsUnseenKnownRow()
    {
        var server = new FakeServer();
        await using var device = await DeviceAsync();
        var shared = await AddCustomerAsync(device, "Shared");
        var engine = Engine(device, server);
        await engine.RunAsync(Biz);                                       // device now has a cursor

        var (legacy, _, _) = await AddLegacyRowsAsync(device, "Legacy", withChildren: false);
        server.Customers.Remove(shared);                                  // gone on the server without a tombstone
        await engine.RunAsync(Biz);

        Assert.True(server.Customers.ContainsKey(legacy));                // adopted and uploaded
        Assert.Null(await device.GetRequiredService<ICustomerLocalService>().GetByIdAsync(Biz, shared)); // known + unseen = pruned
        Assert.Empty(await QueueAsync(device));
        var pushes = server.PushLog.Count;
        await engine.RunAsync(Biz);
        Assert.Equal(pushes, server.PushLog.Count);
    }

    // ---------------------------------------------------------------- 2 discard shows what goes with it

    [Fact]
    public async Task DiscardImpact_NeverSyncedCustomer_CountsChildren_AndPromptNamesThem()
    {
        await using var device = await DeviceAsync();
        var customerId = await AddCustomerAsync(device, "Child Parent");
        var m = await AddMeasurementAsync(device, customerId);
        await AddOrderAsync(device, customerId, m, 100m);
        await AddOrderAsync(device, customerId, null, 50m);
        var engine = Engine(device, new FakeServer());
        var customerItem = (await QueueAsync(device)).Single(q => q.EntityType == SyncEntityType.Customer);

        var impact = await engine.GetDiscardImpactAsync(Biz, customerItem.Id);

        Assert.Equal(new DiscardImpact(true, 1, 2), impact);
        var (_, message, accept) = SyncStatusViewModel.DiscardPrompt(impact);
        Assert.Equal("Discard all", accept);
        Assert.Contains("2 orders and 1 measurement", message);
        Assert.Equal("Discard", SyncStatusViewModel.DiscardPrompt(new DiscardImpact(true, 0, 0)).Accept);
        Assert.Contains("version saved", SyncStatusViewModel.DiscardPrompt(new DiscardImpact(false, 0, 0)).Message);
    }

    // ---------------------------------------------------------------- 3 signing in as another account

    [Theory]
    [InlineData(null, false)]   // no prompt wired: refuse
    [InlineData(false, false)]  // user said no
    [InlineData(true, true)]    // user confirmed: wipe and continue
    public async Task Login_DifferentAccount_WithPendingChanges_NeedsConfirmation(bool? answer, bool expectSignedIn)
    {
        var storage = new MemoryStorage();
        await storage.SetAsync(AuthService.LocalDataOwnerKey, "OLD|B0");
        var database = new PendingDatabase(3);
        var json = """{"accessToken":"a","refreshToken":"r","user":{"userId":"U2","email":"x@y.z","firstName":"A","surname":"B","businessId":"B2","businessName":"Biz","role":"Owner"}}""";
        var auth = new AuthService(new StubFactory(new StubHandler(HttpStatusCode.OK, json)), storage, database, Options.Create(new ApiSettings()));
        int? asked = null;
        if (answer is { } a)
        {
            auth.OtherAccountDataPrompt = n => { asked = n; return Task.FromResult(a); };
        }

        var user = await auth.LoginAsync(new LoginRequest("x@y.z", "pw"));

        Assert.Equal(expectSignedIn, user is not null);
        Assert.Equal(answer is null ? null : 3, asked);
        Assert.Equal(expectSignedIn ? 1 : 0, database.Wipes);
        Assert.Equal(expectSignedIn ? "a" : null, await storage.GetAsync(AuthService.AccessTokenKey));
        if (!expectSignedIn)
        {
            Assert.Equal(AuthService.OtherAccountCancelledMessage, auth.LastErrorMessage);
        }
    }

    [Fact]
    public async Task CountUnsynced_IncludesQueuedAndUnadoptedRows()
    {
        await using var device = await DeviceAsync();
        await AddCustomerAsync(device, "Queued");
        await AddLegacyRowsAsync(device, "Legacy");
        var service = new LocalDatabaseService(device.GetRequiredService<IServiceScopeFactory>(), new LocalDbOptions { DbPath = "unused" });
        Assert.Equal(4, await service.CountUnsyncedAsync());
    }

    // ---------------------------------------------------------------- 6 input rules on device

    [Theory]
    [InlineData(MeasurementUnit.Centimetres, 300.0, true)]
    [InlineData(MeasurementUnit.Centimetres, 300.5, false)]
    [InlineData(MeasurementUnit.Inches, 120.0, true)]
    [InlineData(MeasurementUnit.Inches, 121.0, false)]
    [InlineData(MeasurementUnit.Centimetres, 0.0, false)]
    [InlineData(MeasurementUnit.Centimetres, -5.0, false)]
    public async Task Measurement_OutOfRange_IsRejectedOnDevice(string unit, double value, bool ok)
    {
        await using var device = await DeviceAsync();
        var customerId = await AddCustomerAsync(device, "Range");
        var (m, error) = await device.GetRequiredService<IMeasurementLocalService>().SaveAsync(Biz, customerId, new CreateMeasurementRequest
        {
            GarmentTemplate = GarmentTemplate.Shirt,
            Unit = unit,
            Fields = new Dictionary<string, decimal?> { ["chest"] = (decimal)value }
        });
        Assert.Equal(ok, m is not null);
        Assert.Equal(ok, error is null);
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(-1.0, false)]
    [InlineData(10.555, false)]
    [InlineData(1_000_000.0, true)]
    [InlineData(1_000_000.01, false)]
    [InlineData(10.5, true)]
    public async Task Order_UnitPriceRules_AreEnforcedOnDevice(double price, bool ok)
    {
        await using var device = await DeviceAsync();
        var customerId = await AddCustomerAsync(device, "Price");
        var (o, error) = await device.GetRequiredService<IOrderLocalService>().SaveAsync(Biz, new CreateOrderRequest
        {
            CustomerId = customerId,
            TotalAmount = (decimal)price,
            Lines = [new CreateOrderLineRequest { GarmentTemplate = GarmentTemplate.Shirt, Quantity = 1, UnitPrice = (decimal)price, Unit = MeasurementUnit.Centimetres, MeasurementFields = new Dictionary<string, decimal?>() }]
        });
        Assert.Equal(ok, o is not null);
        Assert.Equal(ok, error is null);
    }

    [Fact]
    public async Task NewOrder_PastDueDate_IsAllowedButWarns()
    {
        await using var device = await DeviceAsync();
        var vm = new CreateOrderViewModel(device.GetRequiredService<IOrderLocalService>(), device.GetRequiredService<ICustomerLocalService>(),
            device.GetRequiredService<IMeasurementLocalService>(), new FakeAuth());
        var today = NeedleBase.Application.Common.BusinessTime.Today(DateTimeOffset.UtcNow).ToDateTime(TimeOnly.MinValue);

        vm.DueDate = today;
        Assert.False(vm.HasDueDateWarning);
        vm.DueDate = today.AddDays(-1);
        Assert.Equal("Due date is in the past.", vm.DueDateWarning);
        vm.HasDueDate = false;
        Assert.False(vm.HasDueDateWarning);
    }

    // ---------------------------------------------------------------- 7 server epoch change (restored database)

    [Fact]
    public async Task EpochChanged_RecoverResync_ReuploadsRowsTheServerLost()
    {
        var server = new FakeServer();
        await using var device = await DeviceAsync();
        var lost = await AddCustomerAsync(device, "Lost In Restore");
        var engine = Engine(device, server);
        await engine.RunAsync(Biz);
        server.Customers.Remove(lost);                    // server restored from an older backup
        server.ResetOnce = true;

        await engine.RunAsync(Biz);

        Assert.True(server.Customers.ContainsKey(lost));
        Assert.NotNull(await device.GetRequiredService<ICustomerLocalService>().GetByIdAsync(Biz, lost));
        Assert.Empty(await QueueAsync(device));
        var pulls = server.Pulls;
        await engine.RunAsync(Biz);                       // no reset loop
        Assert.Equal(pulls + 1, server.Pulls);
    }

    [Fact]
    public async Task ExpiredCursor_NotAReset_StillPrunesKnownRows()
    {
        var server = new FakeServer();
        await using var device = await DeviceAsync();
        var gone = await AddCustomerAsync(device, "Gone");
        var engine = Engine(device, server);
        await engine.RunAsync(Biz);
        server.Customers.Remove(gone);
        server.CursorInvalidOnce = true;

        await engine.RunAsync(Biz);

        Assert.Null(await device.GetRequiredService<ICustomerLocalService>().GetByIdAsync(Biz, gone));
        Assert.False(server.Customers.ContainsKey(gone));
    }

    // ---------------------------------------------------------------- 8 rate limit

    [Fact]
    public async Task RateLimited_ShortRetryAfter_WaitsInline_AndFinishesRun()
    {
        var server = new FakeServer { RateLimitPulls = 2, RetryAfter = TimeSpan.FromSeconds(2) };
        await using var device = await DeviceAsync();
        await AddCustomerAsync(device, "Rated");
        var engine = Engine(device, server);
        var waits = new List<TimeSpan>();
        engine.Delay = (t, _) => { waits.Add(t); return Task.CompletedTask; };

        await engine.RunAsync(Biz);

        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)], waits);
        Assert.Empty(await QueueAsync(device));
        Assert.NotNull((await engine.GetSummaryAsync(Biz)).LastSyncUtc);
    }

    [Fact]
    public async Task RateLimited_LongRetryAfter_EndsRun_ForNormalBackoff()
    {
        var server = new FakeServer { RateLimitPulls = 1, RetryAfter = TimeSpan.FromMinutes(5) };
        await using var device = await DeviceAsync();
        var engine = Engine(device, server);
        engine.Delay = (_, _) => throw new Xunit.Sdk.XunitException("should not wait inline");
        var ex = await Assert.ThrowsAsync<SyncTransientException>(() => engine.RunAsync(Biz));
        Assert.True(ex.RateLimited);
    }

    [Fact]
    public async Task HttpSyncApi_EpochChanged_IsServerReset_PlainCursorInvalidIsNot_429IsRateLimited()
    {
        static HttpSyncApi Api(HttpStatusCode status, string body, TimeSpan? retryAfter = null) =>
            new(new StubFactory(new StubHandler(status, body) { RetryAfter = retryAfter }), Options.Create(new ApiSettings()));

        var epoch = await Assert.ThrowsAsync<SyncCursorInvalidException>(() => Api(HttpStatusCode.BadRequest,
            """{"code":"SYNC_EPOCH_CHANGED","reset":"SYNC_CURSOR_INVALID"}""").PullAsync(new SyncPullRequest(), CancellationToken.None));
        Assert.True(epoch.ServerReset);
        var plain = await Assert.ThrowsAsync<SyncCursorInvalidException>(() => Api(HttpStatusCode.BadRequest,
            """{"code":"SYNC_CURSOR_INVALID"}""").PullAsync(new SyncPullRequest(), CancellationToken.None));
        Assert.False(plain.ServerReset);
        var limited = await Assert.ThrowsAsync<SyncTransientException>(() => Api(HttpStatusCode.TooManyRequests, "{}", TimeSpan.FromSeconds(3))
            .PullAsync(new SyncPullRequest(), CancellationToken.None));
        Assert.True(limited.RateLimited);
        var busy = await Assert.ThrowsAsync<SyncTransientException>(() => Api(HttpStatusCode.ServiceUnavailable, "{}")
            .PullAsync(new SyncPullRequest(), CancellationToken.None));
        Assert.False(busy.RateLimited);
    }

    // ---------------------------------------------------------------- 9 sign-out waits for the running sync

    [Fact]
    public async Task StopAsync_CancelsAndAwaitsRunningSync_ThenBlocksRunsUntilStart()
    {
        var api = new CountingApi { Delay = TimeSpan.FromSeconds(30) };
        await using var device = await DeviceAsync();
        var service = new SyncService(Engine(device, api), new FakeAuth(), new FakeConnectivity());
        var run = service.RunAsync();
        while (api.Pulls == 0)
        {
            await Task.Delay(10);
        }

        var stop = service.StopAsync();
        Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(5000)));   // no deadlock with the gate lock
        Assert.True(run.IsCompleted);
        Assert.True(service.IsStopped);

        await service.RunAsync();
        Assert.Equal(1, api.Pulls);                                      // blocked while stopped

        await service.StopAsync();                                       // idempotent when idle
        service.Start();
        Assert.False(service.IsStopped);
    }

    // ---------------------------------------------------------------- 11 storage

    [Fact]
    public void StorageFull_IsRecognised_AndConnectionHasBusyTimeout()
    {
        Assert.True(LocalStorageErrors.IsStorageFull(new SqliteException("database or disk is full", 13)));
        Assert.True(LocalStorageErrors.IsStorageFull(new DbUpdateException("x", new SqliteException("database or disk is full", 13))));
        Assert.False(LocalStorageErrors.IsStorageFull(new SqliteException("locked", 5)));
        Assert.Equal(LocalStorageErrors.StorageFullMessage, LocalStorageErrors.Describe(new SqliteException("full", 13)));
        var builder = new SqliteConnectionStringBuilder(LocalDbConnection.Build("/x/a.db3"));
        Assert.Equal(30, builder.DefaultTimeout);
    }

    // ---------------------------------------------------------------- 12 order lines snapshot measurements

    [Fact]
    public async Task OrderLine_KeepsMeasurementSnapshot_AfterProfileDeleted()
    {
        var server = new FakeServer();
        await using var device = await DeviceAsync();
        var customerId = await AddCustomerAsync(device, "Snap");
        var m = await AddMeasurementAsync(device, customerId);
        var orderId = await AddOrderAsync(device, customerId, m, 80m);
        await using (var scope = device.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            db.SuppressSyncQueue = true;
            await db.MeasurementProfiles.Where(x => x.Id == m).ExecuteDeleteAsync();   // e.g. deleted elsewhere and pulled
        }

        var order = await device.GetRequiredService<IOrderLocalService>().GetByIdAsync(Biz, orderId);
        Assert.Equal(101.25m, Assert.Single(order!.Lines).MeasurementFields["chest"]);
        await Engine(device, server).RunAsync(Biz);
        Assert.Empty(await QueueAsync(device));
    }

    // ================================================================ helpers

    /// <summary>Writes rows the way a pre-sync build did: no queue items, not server-known.</summary>
    private static async Task<(string Customer, string Measurement, string Order)> AddLegacyRowsAsync(
        ServiceProvider device, string name, bool withChildren = true)
    {
        await using var scope = device.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        db.SuppressSyncQueue = true;
        var queuedBefore = await db.SyncQueue.CountAsync();
        var now = DateTimeOffset.UtcNow;
        var c = NeedleBase.Domain.Entities.Customer.Create(NeedleBase.Application.Common.IdGenerator.NewId(), Biz, name, "+233245550123", null, null, now);
        db.Customers.Add(c);
        string mId = string.Empty, oId = string.Empty;
        if (withChildren)
        {
            var m = MeasurementProfile.Create(NeedleBase.Application.Common.IdGenerator.NewId(), Biz, c.Id, GarmentTemplate.Shirt, MeasurementUnit.Centimetres,
                "{\"chest\":100}", null, now, now);
            db.MeasurementProfiles.Add(m);
            var orderId = NeedleBase.Application.Common.IdGenerator.NewId();
            var line = OrderLine.Create(NeedleBase.Application.Common.IdGenerator.NewId(), Biz, orderId, GarmentTemplate.Shirt, 1, 40m, m.Id,
                MeasurementUnit.Centimetres, "{\"chest\":100}", null, 0);
            var order = NeedleBase.Domain.Entities.Order.Create(orderId, Biz, c.Id, null, null, null, null, [line], now);
            db.Orders.Add(order);
            mId = m.Id;
            oId = order.Id;
        }

        await db.SaveChangesAsync();
        Assert.Equal(queuedBefore, await db.SyncQueue.CountAsync());
        return (c.Id, mId, oId);
    }

    private sealed class PendingDatabase(int pending) : ILocalDatabaseService
    {
        public int Wipes;
        public Task MigrateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> WipeAsync(CancellationToken cancellationToken = default) { Wipes++; return Task.FromResult(true); }
        public Task<int> CountUnsyncedAsync(CancellationToken cancellationToken = default) => Task.FromResult(pending);
    }
}
