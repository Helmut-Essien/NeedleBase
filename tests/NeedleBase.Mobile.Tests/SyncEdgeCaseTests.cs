using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Configuration;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.Helpers;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.Tests;

/// <summary>Regression tests for the Slice 4 edge-case audit (one per finding) + order/measurement round trips.</summary>
public class SyncEdgeCaseTests
{
    private const string Biz = "biz-edge";

    // ---------------------------------------------------------------- orders + measurements round trip

    [Fact]
    public async Task OrderAndMeasurement_PushThenFreshDevicePull_RoundTripsFieldsAndServerIds()
    {
        var server = new FakeServer();
        await using var phone = await DeviceAsync();
        var customerId = await AddCustomerAsync(phone, "Efua");
        var measurementId = await AddMeasurementAsync(phone, customerId);
        var orderId = await AddOrderAsync(phone, customerId, measurementId, unitPrice: 412.5m);
        await phone.GetRequiredService<IOrderLocalService>().UpdateStatusAsync(Biz, orderId, OrderStatus.Cutting);

        await Engine(phone, server).RunAsync(Biz);

        Assert.Empty(await QueueAsync(phone));
        var serverOrder = server.Orders[orderId];
        Assert.Equal(OrderStatus.Cutting, serverOrder.Status);
        Assert.StartsWith("S-", serverOrder.DisplayRef);
        await using (var scope = phone.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            var line = await db.OrderLines.AsNoTracking().SingleAsync(l => l.OrderId == orderId);
            Assert.Equal(serverOrder.Lines[0].Id, line.Id); // server line ids replace local ones on ack
            Assert.Equal(serverOrder.DisplayRef, (await db.Orders.AsNoTracking().SingleAsync()).DisplayRef);
        }

        await using var tablet = await DeviceAsync();
        await Engine(tablet, server).RunAsync(Biz);
        var pulled = await tablet.GetRequiredService<IOrderLocalService>().GetByIdAsync(Biz, orderId);
        Assert.NotNull(pulled);
        Assert.Equal(412.5m, pulled!.TotalAmount);
        Assert.Equal(OrderStatus.Cutting, pulled.Status);
        Assert.Equal(412.5m, Assert.Single(pulled.Lines).UnitPrice);
        var m = await tablet.GetRequiredService<IMeasurementLocalService>().GetByIdAsync(Biz, measurementId);
        Assert.Equal(101.25m, m!.Fields["chest"]);
        Assert.Empty(await QueueAsync(tablet));
    }

    // ---------------------------------------------------------------- 1 resurrection

    [Fact]
    public async Task StaleTombstone_ForRecreatedRecord_IsIgnored()
    {
        var t = DateTimeOffset.UtcNow;
        var api = new Pages(Page(customers: [Customer("C1", "Lazarus", t)],
            deleted: [new SyncDeletedItemDto(SyncEntityType.Customer, "C1", t.AddMinutes(-1))]));
        await using var device = await DeviceAsync();
        await Engine(device, api).RunAsync(Biz);
        Assert.NotNull(await device.GetRequiredService<ICustomerLocalService>().GetByIdAsync(Biz, "C1"));
    }

    [Fact]
    public async Task KeepMine_AfterDeletedElsewhere_RecreatesOnServer()
    {
        var server = new FakeServer();
        await using var device = await DeviceAsync();
        var id = await AddCustomerAsync(device, "Kofi");
        var engine = Engine(device, server);
        await engine.RunAsync(Biz);
        server.DeleteCustomer(id);
        await device.GetRequiredService<ICustomerLocalService>().UpdateAsync(Biz, id, new UpdateCustomerRequest("Kofi 2", "0245550123", null, null));

        await engine.RunAsync(Biz);
        var conflict = Assert.Single(await QueueAsync(device));
        Assert.Equal(SyncErrorCodes.Deleted, conflict.ConflictCode);

        Assert.True(await engine.ResolveConflictAsync(Biz, conflict.Id, keepMine: true));
        await engine.RunAsync(Biz);
        await engine.RunAsync(Biz);

        Assert.Equal("Kofi 2", server.Customers[id].Name);
        Assert.NotNull(await device.GetRequiredService<ICustomerLocalService>().GetByIdAsync(Biz, id));
        Assert.Empty(await QueueAsync(device));
    }

    // ---------------------------------------------------------------- 2 dependents of a deleted customer

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CustomerDeletedOnServer_WithLocalOrder_IsAConflict_NotAFailure(bool restore)
    {
        var server = new FakeServer();
        await using var device = await DeviceAsync();
        var customerId = await AddCustomerAsync(device, "Ama");
        var engine = Engine(device, server);
        await engine.RunAsync(Biz);

        server.DeleteCustomer(customerId);                       // deleted on another device
        var orderId = await AddOrderAsync(device, customerId, null, 100m); // created here, offline
        await engine.RunAsync(Biz);

        var queue = await QueueAsync(device);
        Assert.DoesNotContain(queue, q => q.State == SyncQueueState.Failed);
        var orderItem = Assert.Single(queue, q => q.EntityType == SyncEntityType.Order);
        Assert.Equal(SyncErrorCodes.ParentDeleted, orderItem.ConflictCode);
        var customerItem = Assert.Single(queue, q => q.EntityType == SyncEntityType.Customer);
        Assert.Equal(SyncErrorCodes.Deleted, customerItem.ConflictCode);

        Assert.True(await engine.ResolveConflictAsync(Biz, orderItem.Id, keepMine: restore));
        if (!restore)
        {
            Assert.True(await engine.ResolveConflictAsync(Biz, customerItem.Id, keepMine: false));
        }

        await engine.RunAsync(Biz);
        await engine.RunAsync(Biz);

        Assert.Empty(await QueueAsync(device));
        var local = await device.GetRequiredService<ICustomerLocalService>().GetByIdAsync(Biz, customerId);
        if (restore)
        {
            Assert.NotNull(local);
            Assert.True(server.Customers.ContainsKey(customerId));
            Assert.True(server.Orders.ContainsKey(orderId));
        }
        else
        {
            Assert.Null(local);
            Assert.Null(await device.GetRequiredService<IOrderLocalService>().GetByIdAsync(Biz, orderId));
            Assert.False(server.Orders.ContainsKey(orderId));
        }
    }

    [Fact]
    public async Task CustomerTombstone_WithPendingMeasurement_LeavesNoOrphanFailure_AndDeleteHereCleansUp()
    {
        var server = new FakeServer();
        await using var device = await DeviceAsync();
        var customerId = await AddCustomerAsync(device, "Esi");
        var engine = Engine(device, server);
        await engine.RunAsync(Biz);
        server.DeleteCustomer(customerId);
        await AddMeasurementAsync(device, customerId);

        await engine.RunAsync(Biz);
        var queue = await QueueAsync(device);
        Assert.Equal(2, queue.Count);
        Assert.All(queue, q => Assert.Equal(SyncQueueState.Conflict, q.State));

        var customerItem = queue.Single(q => q.EntityType == SyncEntityType.Customer);
        Assert.True(await engine.ResolveConflictAsync(Biz, customerItem.Id, keepMine: false)); // "Delete here"

        Assert.Empty(await QueueAsync(device));
        Assert.Equal(0, (await engine.GetSummaryAsync(Biz)).NeedsAttention);
        Assert.Null(await device.GetRequiredService<ICustomerLocalService>().GetByIdAsync(Biz, customerId));
    }

    [Fact]
    public async Task DeleteRefused_HasOrders_RestoresCustomerAndItsOrders()
    {
        var server = new FakeServer();
        await using var device = await DeviceAsync();
        var customerId = await AddCustomerAsync(device, "Yaw");
        var engine = Engine(device, server);
        await engine.RunAsync(Biz);
        server.AddOrderFor(customerId);                         // another device added an order
        await device.GetRequiredService<ICustomerLocalService>().DeleteAsync(Biz, customerId); // no local orders yet

        await engine.PushAsync(Biz);
        var item = Assert.Single(await QueueAsync(device));
        Assert.Equal(SyncErrorCodes.DeleteRefused, item.ConflictCode);

        Assert.True(await engine.ResolveConflictAsync(Biz, item.Id, keepMine: false));
        await engine.RunAsync(Biz);

        Assert.NotNull(await device.GetRequiredService<ICustomerLocalService>().GetByIdAsync(Biz, customerId));
        Assert.Single(await device.GetRequiredService<IOrderLocalService>().SearchAsync(Biz, string.Empty, null));
        Assert.Empty(await QueueAsync(device));
    }

    // ---------------------------------------------------------------- 3 deferred children

    [Fact]
    public async Task PulledChild_WithMissingCustomer_IsDeferred_ThenAppliedWhenCustomerArrives()
    {
        var t = DateTimeOffset.UtcNow;
        var order = Order("O1", "C9", t);
        var api = new Pages(
            Page(orders: [order], hasMore: true),
            Page(customers: [Customer("C9", "Late", t)]));
        await using var device = await DeviceAsync();
        var engine = Engine(device, api);

        await engine.PullAsync(Biz);

        Assert.NotNull(await device.GetRequiredService<IOrderLocalService>().GetByIdAsync(Biz, "O1"));
        await using var scope = device.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<LocalDbContext>().SyncDeferred.ToListAsync());
    }

    [Fact]
    public async Task PulledChild_WithMissingCustomer_IsKeptUntilParentArrives()
    {
        var t = DateTimeOffset.UtcNow;
        await using var device = await DeviceAsync();
        await Engine(device, new Pages(Page(measurements: [Measurement("M1", "C7", t)]))).PullAsync(Biz);
        await using (var scope = device.CreateAsyncScope())
        {
            Assert.Single(await scope.ServiceProvider.GetRequiredService<LocalDbContext>().SyncDeferred.ToListAsync());
        }

        await Engine(device, new Pages(Page(customers: [Customer("C7", "Kweku", t)]))).PullAsync(Biz);
        Assert.NotNull(await device.GetRequiredService<IMeasurementLocalService>().GetByIdAsync(Biz, "M1"));
    }

    // ---------------------------------------------------------------- 4 discard

    [Fact]
    public async Task DiscardFailedCreate_RemovesItFromThisDevice()
    {
        var server = new FakeServer { FailAll = true };
        await using var device = await DeviceAsync();
        var id = await AddCustomerAsync(device, "Bad");
        var engine = Engine(device, server);
        await engine.RunAsync(Biz);
        var item = Assert.Single(await QueueAsync(device));
        Assert.Equal(SyncQueueState.Failed, item.State);

        await engine.DiscardAsync(Biz, item.Id);

        Assert.Empty(await QueueAsync(device));
        Assert.Null(await device.GetRequiredService<ICustomerLocalService>().GetByIdAsync(Biz, id));
    }

    [Fact]
    public async Task DiscardFailedEdit_RestoresServerCopy()
    {
        var server = new FakeServer();
        await using var device = await DeviceAsync();
        var id = await AddCustomerAsync(device, "Good");
        var engine = Engine(device, server);
        await engine.RunAsync(Biz);
        server.FailAll = true;
        await device.GetRequiredService<ICustomerLocalService>().UpdateAsync(Biz, id, new UpdateCustomerRequest("Bad edit", "0245550123", null, null));
        await engine.RunAsync(Biz);
        var item = Assert.Single(await QueueAsync(device));

        server.FailAll = false;
        await engine.DiscardAsync(Biz, item.Id);
        await engine.RunAsync(Biz);

        Assert.Equal("Good", (await device.GetRequiredService<ICustomerLocalService>().GetByIdAsync(Biz, id))!.Name);
        Assert.Empty(await QueueAsync(device));
    }

    // ---------------------------------------------------------------- 8 resync, ordering, transactions

    [Fact]
    public async Task InvalidCursor_ResyncsAndPrunesRowsTheServerNoLongerHas_KeepingQueuedWork()
    {
        var server = new FakeServer();
        await using var device = await DeviceAsync();
        var gone = await AddCustomerAsync(device, "Gone");
        var engine = Engine(device, server);
        await engine.RunAsync(Biz);
        server.Customers.Remove(gone); // deleted and its tombstone purged (older than retention)

        var held = await AddCustomerAsync(device, "Held for review");
        await using (var scope = device.CreateAsyncScope())
        {
            // A change waiting on the user (not pushed this run) must survive the prune.
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            (await db.SyncQueue.SingleAsync()).State = SyncQueueState.Conflict;
            db.SuppressSyncQueue = true;
            await db.SaveChangesAsync();
        }

        server.CursorInvalidOnce = true;
        await engine.RunAsync(Biz);

        var customers = device.GetRequiredService<ICustomerLocalService>();
        Assert.Null(await customers.GetByIdAsync(Biz, gone));
        Assert.NotNull(await customers.GetByIdAsync(Biz, held));
        await using var check = device.CreateAsyncScope();
        Assert.False((await check.ServiceProvider.GetRequiredService<LocalDbContext>().DeviceInfo.SingleAsync()).ResyncPending);
    }

    [Fact]
    public async Task Push_SendsParentsBeforeChildren()
    {
        var server = new FakeServer();
        await using var device = await DeviceAsync();
        var customerId = await AddCustomerAsync(device, "Order first");
        await using (var scope = device.CreateAsyncScope())
        {
            // Simulate a customer item queued after its measurement (e.g. restored later).
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            db.SyncQueue.Remove(await db.SyncQueue.SingleAsync());
            await db.SaveChangesAsync();
            db.SyncQueue.Add(new SyncQueueItem
            {
                Id = "ZZZZZZZZZZZZZZZZZZZZZZZZZZ", BusinessId = Biz, EntityType = SyncEntityType.Customer, EntityId = customerId,
                Operation = SyncOperation.Upsert, Label = "x", State = SyncQueueState.Pending,
                PayloadJson = SyncJson.Serialize(new SyncCustomerDto(customerId, "Order first", "0245550123", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow))
            });
            db.SuppressSyncQueue = true;
            await db.SaveChangesAsync();
        }

        await AddMeasurementAsync(device, customerId);
        await Engine(device, server).PushAsync(Biz);

        Assert.Equal([SyncEntityType.Customer, SyncEntityType.Measurement], server.PushLog);
        Assert.Empty(await QueueAsync(device));
    }

    [Fact]
    public async Task PullPage_AndCursor_AreSavedTogether_FailureOnLaterPageKeepsEarlierProgress()
    {
        var t = DateTimeOffset.UtcNow;
        var api = new Pages(Page(customers: [Customer("C1", "One", t)], hasMore: true, cursor: "p1")) { FailAfter = 1 };
        await using var device = await DeviceAsync();
        await Assert.ThrowsAsync<SyncTransientException>(() => Engine(device, api).PullAsync(Biz));

        await using var scope = device.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        Assert.Equal("p1", (await db.DeviceInfo.SingleAsync()).PullCursor);
        Assert.Single(await db.Customers.ToListAsync());
    }

    [Fact]
    public void UnknownStatus_ShowsNeutralLabel()
    {
        Assert.Equal(OrderStatusPresentation.UnknownStatusLabel, OrderStatusPresentation.DisplayName("archived"));
        Assert.Equal("Ready to collect", OrderStatusPresentation.DisplayName(OrderStatus.Ready));
    }

    // ---------------------------------------------------------------- 6 + Retry-After (HTTP)

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "{}", typeof(SyncAccessDeniedException))]
    [InlineData(HttpStatusCode.BadRequest, "{\"code\":\"VALIDATION_FAILED\"}", typeof(SyncRejectedException))]
    [InlineData(HttpStatusCode.BadRequest, "not json", typeof(SyncRejectedException))]
    public async Task HttpSyncApi_NonRetryableFailures(HttpStatusCode status, string body, Type expected)
    {
        var api = new HttpSyncApi(new StubFactory(new StubHandler(status, body)), Options.Create(new ApiSettings()));
        Assert.IsType(expected, await Record.ExceptionAsync(() => api.PushAsync(new SyncPushRequest(), CancellationToken.None)));
    }

    [Fact]
    public async Task HttpSyncApi_429_CarriesRetryAfter()
    {
        var handler = new StubHandler(HttpStatusCode.TooManyRequests, "{}") { RetryAfter = TimeSpan.FromSeconds(90) };
        var api = new HttpSyncApi(new StubFactory(handler), Options.Create(new ApiSettings()));
        var ex = await Assert.ThrowsAsync<SyncTransientException>(() => api.PullAsync(new SyncPullRequest(), CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(90), ex.RetryAfter);
    }

    // ---------------------------------------------------------------- orchestrator (SyncService)

    [Fact]
    public async Task Service_Forbidden_ShowsNoAccess_AndLocalSaveDoesNotRetry()
    {
        var api = new CountingApi { Throw = () => new SyncAccessDeniedException() };
        await using var device = await DeviceAsync();
        var service = new SyncService(Engine(device, api), new FakeAuth(), new FakeConnectivity());
        await service.RunAsync();
        Assert.Equal(SyncChipState.NoAccess, service.Current.State);

        service.OnQueueChanged(this, EventArgs.Empty);
        await Task.Delay(2500);
        Assert.Equal(1, api.Pulls);

        await service.SyncNowAsync(); // manual retry is allowed
        Assert.Equal(2, api.Pulls);
    }

    [Fact]
    public async Task Service_BadRequest_ShowsBlocked()
    {
        var api = new CountingApi { Throw = () => new SyncRejectedException("X") };
        await using var device = await DeviceAsync();
        var service = new SyncService(Engine(device, api), new FakeAuth(), new FakeConnectivity());
        await service.RunAsync();
        Assert.Equal(SyncChipState.Blocked, service.Current.State);
    }

    [Fact]
    public async Task Service_TransientBackoff_HonoursRetryAfter_AndLocalSaveDoesNotCutItShort()
    {
        var now = DateTimeOffset.UtcNow;
        var api = new CountingApi { Throw = () => new SyncTransientException("busy", retryAfter: TimeSpan.FromMinutes(10)) };
        await using var device = await DeviceAsync();
        var service = new SyncService(Engine(device, api), new FakeAuth(), new FakeConnectivity(), () => now);
        await service.RunAsync();
        Assert.True(service.IsBackingOff);

        now = now.AddMinutes(9);
        service.OnQueueChanged(this, EventArgs.Empty);
        await Task.Delay(2500);
        Assert.Equal(1, api.Pulls);
        Assert.True(service.IsBackingOff);
        now = now.AddMinutes(2);
        Assert.False(service.IsBackingOff);
    }

    [Fact]
    public async Task Service_SessionExpired_ShowsSignIn_KeepsQueue()
    {
        var api = new CountingApi { Throw = () => new SyncSessionExpiredException(), ThrowOnPush = true };
        await using var device = await DeviceAsync();
        await AddCustomerAsync(device, "Kept");
        var service = new SyncService(Engine(device, api), new FakeAuth(), new FakeConnectivity());
        await service.RunAsync();
        Assert.Equal(SyncChipState.SignInNeeded, service.Current.State);
        Assert.Single(await QueueAsync(device));
    }

    [Fact]
    public async Task Service_ConcurrentTriggers_NeverRunTwoAtOnce()
    {
        var api = new CountingApi { Delay = TimeSpan.FromMilliseconds(50) };
        await using var device = await DeviceAsync();
        var service = new SyncService(Engine(device, api), new FakeAuth(), new FakeConnectivity());
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(service.RunAsync)));
        await Task.Delay(300);
        Assert.Equal(1, api.MaxConcurrent);
        Assert.InRange(api.Pulls, 1, 2); // one run plus at most one coalesced re-run
    }

    // ---------------------------------------------------------------- 5 re-sign-in + shared refresh lock

    [Fact]
    public async Task SignInAgain_SameAccount_KeepsLocalData_DifferentAccountWipes()
    {
        var storage = new MemoryStorage();
        var database = new CountingDatabase();
        var auth = new AuthService(new StubFactory(new StubHandler(HttpStatusCode.OK, "{}")), storage, database, Options.Create(new ApiSettings()));
        var user = new AuthUserDto("U1", "a@b.c", "A", "B", "B1", "Biz", "Owner");

        await auth.StoreSessionAsync(user);
        await auth.ClearTokensAsync();        // session expired: tokens gone, data kept
        await auth.StoreSessionAsync(user);   // same account signs in again
        Assert.Equal(0, database.Wipes);

        await auth.ClearTokensAsync();
        await auth.StoreSessionAsync(user with { UserId = "U2" });
        Assert.Equal(1, database.Wipes);
        await auth.StoreSessionAsync(user with { UserId = "U2", BusinessId = "B2" });
        Assert.Equal(2, database.Wipes);
    }

    [Fact]
    public async Task ConcurrentRefreshIfStale_RefreshesOnce()
    {
        var storage = new MemoryStorage();
        await storage.SetAsync(AuthService.AccessTokenKey, "old");
        await storage.SetAsync(AuthService.RefreshTokenKey, "r1");
        var handler = new RefreshHandler();
        var auth = new AuthService(new StubFactory(handler), storage, new CountingDatabase(), Options.Create(new ApiSettings()));

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => auth.RefreshIfStaleAsync("old")));

        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.Equal(1, handler.Calls);
        Assert.Equal("new-1", await storage.GetAsync(AuthService.AccessTokenKey));
    }

    // ================================================================ helpers

    private static SyncEngine Engine(ServiceProvider device, ISyncApi api) => new(device.GetRequiredService<IServiceScopeFactory>(), api);

    private static SyncCustomerDto Customer(string id, string name, DateTimeOffset t) => new(id, name, "0245550123", null, null, t, t);

    private static SyncMeasurementDto Measurement(string id, string customerId, DateTimeOffset t) =>
        new(id, customerId, GarmentTemplate.Shirt, MeasurementUnit.Centimetres, "{\"chest\":100}", null, t, t);

    private static SyncOrderDto Order(string id, string customerId, DateTimeOffset t) =>
        new(id, customerId, "0042", OrderStatus.New, null, null, null, 50m, null, t, t,
            [new SyncOrderLineDto(id + "-L", GarmentTemplate.Shirt, 1, 50m, null, MeasurementUnit.Centimetres, "{}", null, 0)]);

    private static SyncPullResponse Page(
        SyncCustomerDto[]? customers = null, SyncMeasurementDto[]? measurements = null, SyncOrderDto[]? orders = null,
        SyncDeletedItemDto[]? deleted = null, bool hasMore = false, string cursor = "c") =>
        new(customers ?? [], measurements ?? [], orders ?? [], deleted ?? [], cursor, hasMore, DateTimeOffset.UtcNow);

    private static async Task<string> AddCustomerAsync(ServiceProvider device, string name)
    {
        var (c, error) = await device.GetRequiredService<ICustomerLocalService>()
            .SaveAsync(Biz, new CreateCustomerRequest(name, "0245550123", null, null));
        Assert.Null(error);
        return c!.Id;
    }

    private static async Task<string> AddMeasurementAsync(ServiceProvider device, string customerId)
    {
        var (m, error) = await device.GetRequiredService<IMeasurementLocalService>().SaveAsync(Biz, customerId,
            new CreateMeasurementRequest
            {
                GarmentTemplate = GarmentTemplate.Shirt,
                Unit = MeasurementUnit.Centimetres,
                Fields = new Dictionary<string, decimal?> { ["chest"] = 101.25m }
            });
        Assert.Null(error);
        return m!.Id;
    }

    private static async Task<string> AddOrderAsync(ServiceProvider device, string customerId, string? measurementId, decimal unitPrice)
    {
        var (o, error) = await device.GetRequiredService<IOrderLocalService>().SaveAsync(Biz, new CreateOrderRequest
        {
            CustomerId = customerId,
            TotalAmount = unitPrice,
            Lines =
            [
                new CreateOrderLineRequest
                {
                    GarmentTemplate = GarmentTemplate.Shirt, Quantity = 1, UnitPrice = unitPrice, MeasurementProfileId = measurementId,
                    Unit = MeasurementUnit.Centimetres, MeasurementFields = new Dictionary<string, decimal?>()
                }
            ]
        });
        Assert.Null(error);
        return o!.Id;
    }

    private static async Task<List<SyncQueueItem>> QueueAsync(ServiceProvider device)
    {
        await using var scope = device.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LocalDbContext>().SyncQueue.AsNoTracking().ToListAsync();
    }

    private static async Task<ServiceProvider> DeviceAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"needlebase-edge-{Guid.NewGuid():N}.db3");
        var services = new ServiceCollection();
        services.AddDbContext<LocalDbContext>(o => o.UseSqlite($"Data Source={path};Pooling=False"));
        services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
        services.AddSingleton<IMeasurementLocalService, MeasurementLocalService>();
        services.AddSingleton<IOrderLocalService, OrderLocalService>();
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await LocalSchemaMigrator.MigrateAsync(scope.ServiceProvider.GetRequiredService<LocalDbContext>());
        return provider;
    }

    /// <summary>In-memory server with the API's rules (concurrency, parent checks, delete refusal, server line ids/refs).</summary>
    private sealed class FakeServer : ISyncApi
    {
        private int _ref;
        private long _tick;
        private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;

        public Dictionary<string, SyncCustomerDto> Customers { get; } = new();
        public Dictionary<string, SyncMeasurementDto> Measurements { get; } = new();
        public Dictionary<string, SyncOrderDto> Orders { get; } = new();
        public List<SyncDeletedItemDto> Deleted { get; } = new();
        public List<string> PushLog { get; } = new();
        public bool FailAll { get; set; }
        public bool FailPushes { get; set; }
        public bool FailPushesOnly { get; set; }
        public bool CursorInvalidOnce { get; set; }

        private DateTimeOffset Now() => _start.AddMilliseconds(++_tick);

        public void DeleteCustomer(string id)
        {
            Customers.Remove(id);
            Deleted.Add(new SyncDeletedItemDto(SyncEntityType.Customer, id, Now()));
            foreach (var m in Measurements.Values.Where(m => m.CustomerId == id).ToList())
            {
                Measurements.Remove(m.Id);
                Deleted.Add(new SyncDeletedItemDto(SyncEntityType.Measurement, m.Id, Now()));
            }
        }

        public void AddOrderFor(string customerId)
        {
            var t = Now();
            var id = "SRV-" + _tick;
            Orders[id] = new SyncOrderDto(id, customerId, "S-" + (++_ref).ToString("0000"), OrderStatus.New, null, null, null, 10m, null, t, t,
                [new SyncOrderLineDto(id + "-L", GarmentTemplate.Shirt, 1, 10m, null, MeasurementUnit.Centimetres, "{}", null, 0)]);
        }

        public Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken)
        {
            if (FailPushes || FailPushesOnly) throw new SyncTransientException("offline");
            var results = new List<SyncPushResult>();
            foreach (var op in request.Operations)
            {
                PushLog.Add(op.EntityType);
                results.Add(FailAll
                    ? new SyncPushResult(op.OpId, SyncOpStatus.Failed, "VALIDATION_FAILED", "Rejected by test.", null, null, null)
                    : Apply(op));
            }

            return Task.FromResult(new SyncPushResponse(results));
        }

        private SyncPushResult Apply(SyncPushOperation op)
        {
            SyncPushResult Ok(SyncCustomerDto? c = null, SyncMeasurementDto? m = null, SyncOrderDto? o = null) =>
                new(op.OpId, SyncOpStatus.Applied, null, null, c, m, o);
            SyncPushResult Conflict(string code, SyncCustomerDto? c = null, SyncMeasurementDto? m = null, SyncOrderDto? o = null) =>
                new(op.OpId, SyncOpStatus.Conflict, code, "conflict", c, m, o);

            switch (op.EntityType)
            {
                case SyncEntityType.Customer:
                {
                    Customers.TryGetValue(op.EntityId, out var existing);
                    if (op.Operation == SyncOperation.Delete)
                    {
                        if (existing is null) return Ok();
                        if (Orders.Values.Any(o => o.CustomerId == op.EntityId)) return Conflict(SyncErrorCodes.DeleteRefused, existing);
                        DeleteCustomer(op.EntityId);
                        return Ok();
                    }

                    if (existing is null && op.BaseModifiedUtc is not null) return Conflict(SyncErrorCodes.Deleted);
                    if (existing is not null && op.BaseModifiedUtc is { } b && b != existing.DateModifiedUtc)
                        return Conflict(SyncErrorCodes.StaleBase, existing);
                    var t = Now();
                    var saved = op.Customer! with { CreatedAtUtc = existing?.CreatedAtUtc ?? t, DateModifiedUtc = t };
                    Customers[op.EntityId] = saved;
                    Deleted.RemoveAll(d => d.EntityId == op.EntityId); // server drops the stale tombstone
                    return Ok(c: saved);
                }

                case SyncEntityType.Measurement:
                {
                    Measurements.TryGetValue(op.EntityId, out var existing);
                    if (op.Operation == SyncOperation.Delete)
                    {
                        Measurements.Remove(op.EntityId);
                        return Ok();
                    }

                    if (!Customers.ContainsKey(op.Measurement!.CustomerId)) return Conflict(SyncErrorCodes.ParentDeleted);
                    if (existing is null && op.BaseModifiedUtc is not null) return Conflict(SyncErrorCodes.Deleted);
                    var saved = op.Measurement with { DateModifiedUtc = Now() };
                    Measurements[op.EntityId] = saved;
                    return Ok(m: saved);
                }

                default:
                {
                    Orders.TryGetValue(op.EntityId, out var existing);
                    if (op.Operation == SyncOperation.Delete)
                    {
                        Orders.Remove(op.EntityId);
                        return Ok();
                    }

                    var dto = op.Order!;
                    if (existing is null)
                    {
                        if (!Customers.ContainsKey(dto.CustomerId)) return Conflict(SyncErrorCodes.ParentDeleted);
                        var t = Now();
                        var saved = dto with
                        {
                            DisplayRef = "S-" + (++_ref).ToString("0000"),
                            CreatedAtUtc = t,
                            DateModifiedUtc = t,
                            Lines = dto.Lines.Select((l, i) => l with { Id = $"SL-{op.EntityId}-{i}" }).ToList()
                        };
                        Orders[op.EntityId] = saved;
                        return Ok(o: saved);
                    }

                    var updated = existing with { Status = dto.Status, Notes = dto.Notes, DueDate = dto.DueDate, DateModifiedUtc = Now() };
                    Orders[op.EntityId] = updated;
                    return Ok(o: updated);
                }
            }
        }

        public Task<SyncPullResponse> PullAsync(SyncPullRequest request, CancellationToken cancellationToken)
        {
            if (FailPushes) throw new SyncTransientException("offline");
            if (CursorInvalidOnce && request.Cursor is not null)
            {
                CursorInvalidOnce = false;
                throw new SyncCursorInvalidException();
            }

            // Like the API: a first pull (no cursor) sends no tombstones.
            return Task.FromResult(new SyncPullResponse(Customers.Values.ToList(), Measurements.Values.ToList(), Orders.Values.ToList(),
                request.Cursor is null ? [] : Deleted.ToList(), "c", false, DateTimeOffset.UtcNow));
        }
    }

    private sealed class Pages(params SyncPullResponse[] pages) : ISyncApi
    {
        private int _i;
        public int FailAfter { get; init; } = int.MaxValue;

        public Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new SyncPushResponse([]));

        public Task<SyncPullResponse> PullAsync(SyncPullRequest request, CancellationToken cancellationToken)
        {
            if (_i >= FailAfter) throw new SyncTransientException("dropped");
            return Task.FromResult(pages[Math.Min(_i++, pages.Length - 1)]);
        }
    }

    private sealed class CountingApi : ISyncApi
    {
        private int _active;
        public Func<Exception>? Throw { get; init; }
        public TimeSpan Delay { get; init; }
        public int Pulls;
        public int Pushes;
        public int MaxConcurrent;

        public bool ThrowOnPush { get; init; }

        public Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Pushes);
            if (ThrowOnPush && Throw is not null) throw Throw();
            return Task.FromResult(new SyncPushResponse(request.Operations
                .Select(o => new SyncPushResult(o.OpId, SyncOpStatus.Applied, null, null, null, null, null)).ToList()));
        }

        public async Task<SyncPullResponse> PullAsync(SyncPullRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Pulls);
            var now = Interlocked.Increment(ref _active);
            lock (this) MaxConcurrent = Math.Max(MaxConcurrent, now);
            try
            {
                await Task.Delay(Delay, cancellationToken);
                if (Throw is not null) throw Throw();
                return new SyncPullResponse([], [], [], [], "c", false, DateTimeOffset.UtcNow);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class FakeConnectivity : IConnectivity
    {
        public IEnumerable<ConnectionProfile> ConnectionProfiles => [ConnectionProfile.WiFi];
        public NetworkAccess NetworkAccess => NetworkAccess.Internet;
        public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged { add { } remove { } }
    }

    private sealed class FakeAuth : IAuthService
    {
        public Task<AuthUserDto?> RegisterBusinessAsync(RegisterBusinessRequest request, CancellationToken cancellationToken = default) => Task.FromResult<AuthUserDto?>(null);
        public Task<AuthUserDto?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) => Task.FromResult<AuthUserDto?>(null);
        public Task<TokenRefreshResult> RefreshAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasStoredTokensAsync() => Task.FromResult(true);
        public Task ClearTokensAsync() => Task.CompletedTask;
        public Task<bool> LogoutAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("t");
        public Task StoreTokensAsync(TokenResponse tokens) => Task.CompletedTask;
        public Task StoreSessionAsync(AuthUserDto user) => Task.CompletedTask;
        public Task<string?> GetBusinessIdAsync() => Task.FromResult<string?>(Biz);
        public Task<AuthUserDto?> GetCachedUserAsync() => Task.FromResult<AuthUserDto?>(null);
    }

    private sealed class MemoryStorage : ISecureStorageService
    {
        private readonly Dictionary<string, string> _values = new();
        public Task SetAsync(string key, string value) { lock (_values) _values[key] = value; return Task.CompletedTask; }
        public Task<string?> GetAsync(string key) { lock (_values) return Task.FromResult(_values.TryGetValue(key, out var v) ? v : null); }
        public void Remove(string key) { lock (_values) _values.Remove(key); }
    }

    private sealed class CountingDatabase : ILocalDatabaseService
    {
        public int Wipes;
        public Task MigrateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> WipeAsync(CancellationToken cancellationToken = default) { Wipes++; return Task.FromResult(true); }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost/") };
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public TimeSpan? RetryAfter { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (RetryAfter is { } ra) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(ra);
            return Task.FromResult(response);
        }
    }

    private sealed class RefreshHandler : HttpMessageHandler
    {
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref Calls);
            await Task.Delay(30, cancellationToken);
            var json = $$"""{"accessToken":"new-{{n}}","refreshToken":"r{{n + 1}}"}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
