using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

/// <summary>Slice 5 on the phone: local rules, queue, push/pull, deferral, conflicts, prune, receipt copy.</summary>
public class PaymentSyncTests
{
    private const string Biz = "biz-pay";

    // ------------------------------------------------------------------ local rules

    [Fact]
    public async Task Record_Deposit_ShowsBalance_AndIsQueuedAsPending()
    {
        await using var device = await DeviceAsync();
        var orderId = await AddOrderAsync(device, 800m);
        var payments = device.GetRequiredService<IPaymentLocalService>();

        var result = await payments.RecordAsync(Biz, orderId, Request(200m, PaymentMethod.Cash));
        Assert.True(result.IsSuccess);

        var money = (await payments.GetForOrderAsync(Biz, orderId))!;
        Assert.Equal(600m, money.Summary.Balance);
        Assert.Equal("GH₵ 200.00 of GH₵ 800.00 paid", money.Summary.PaidSummary);
        Assert.Equal("GH₵ 600.00 left", money.Summary.BalanceDisplay);
        var row = Assert.Single(money.Payments);
        Assert.True(row.IsPending);
        Assert.Equal("Recorded on this device", row.StatusBadge);
        Assert.Equal("Cash payment recorded · " + Money.Date(row.Payment.RecordedAtUtc), row.MethodLine);

        var item = Assert.Single(await QueueAsync(device), q => q.EntityType == SyncEntityType.Payment);
        Assert.Equal(orderId, item.ParentId);
        Assert.Equal(SyncOperation.Upsert, item.Operation);
    }

    [Fact]
    public async Task Record_Rules_Overpay_Cancelled_Decimals_SameIdReplay()
    {
        await using var device = await DeviceAsync();
        var orderId = await AddOrderAsync(device, 100m);
        var payments = device.GetRequiredService<IPaymentLocalService>();

        Assert.Equal(RecordError.PaymentExceedsBalance, (await payments.RecordAsync(Biz, orderId, Request(100.01m, PaymentMethod.Cash))).Error);
        Assert.Equal(RecordError.PaymentInvalid, (await payments.RecordAsync(Biz, orderId, Request(1.005m, PaymentMethod.Cash))).Error);
        Assert.Equal(RecordError.PaymentInvalid, (await payments.RecordAsync(Biz, orderId, Request(10m, "Card"))).Error);
        Assert.Equal(RecordError.OrderNotFound, (await payments.RecordAsync(Biz, "missing", Request(10m, PaymentMethod.Cash))).Error);

        var request = Request(40m, PaymentMethod.MoMo);
        request.Id = IdGenerator.NewId();
        Assert.True((await payments.RecordAsync(Biz, orderId, request)).IsSuccess);
        Assert.True((await payments.RecordAsync(Biz, orderId, request)).IsSuccess); // double tap: same payment
        Assert.Equal(40m, (await payments.GetForOrderAsync(Biz, orderId))!.Summary.PaidAmount);

        await device.GetRequiredService<IOrderLocalService>().UpdateStatusAsync(Biz, orderId, OrderStatus.Cancelled);
        Assert.Equal(RecordError.PaymentOrderLocked, (await payments.RecordAsync(Biz, orderId, Request(10m, PaymentMethod.Cash))).Error);
        Assert.False((await payments.GetForOrderAsync(Biz, orderId))!.CanRecord);
    }

    [Fact]
    public async Task Money_IsExact_ManySmallPaymentsSumWithoutDrift()
    {
        await using var device = await DeviceAsync();
        var orderId = await AddOrderAsync(device, 1m);
        var payments = device.GetRequiredService<IPaymentLocalService>();
        for (var i = 0; i < 10; i++)
        {
            Assert.True((await payments.RecordAsync(Biz, orderId, Request(0.1m, PaymentMethod.Cash))).IsSuccess);
        }

        var money = (await payments.GetForOrderAsync(Biz, orderId))!;
        Assert.Equal(1.0m, money.Summary.PaidAmount); // decimal TEXT in SQLite, summed in C#: no 0.999999
        Assert.True(money.Summary.IsPaidInFull);
        Assert.Equal(RecordError.PaymentExceedsBalance, (await payments.RecordAsync(Biz, orderId, Request(0.01m, PaymentMethod.Cash))).Error);
    }

    [Fact]
    public async Task Void_ExcludesFromBalance_IsIdempotent_AndQueuesTheVoid()
    {
        await using var device = await DeviceAsync();
        var server = new PayServer();
        var orderId = await AddOrderAsync(device, 500m);
        var payments = device.GetRequiredService<IPaymentLocalService>();
        var paymentId = (await payments.RecordAsync(Biz, orderId, Request(500m, PaymentMethod.Bank))).Value!.Id;
        await Engine(device, server).RunAsync(Biz);
        Assert.Empty(await QueueAsync(device));

        Assert.True((await payments.VoidAsync(Biz, paymentId, "wrong order")).Value!.IsVoided);
        Assert.True((await payments.VoidAsync(Biz, paymentId, "again")).IsSuccess);
        var money = (await payments.GetForOrderAsync(Biz, orderId))!;
        Assert.Equal(500m, money.Summary.Balance);
        Assert.Equal("Voided", Assert.Single(money.Payments).StatusBadge);

        await Engine(device, server).RunAsync(Biz);
        Assert.True(server.Payments[paymentId].IsVoided);
        Assert.Equal("wrong order", server.Payments[paymentId].VoidReason);
    }

    // ------------------------------------------------------------------ sync

    [Fact]
    public async Task Push_SendsPaymentsAfterTheirOrder_ThenFreshDevicePullsSameBalance()
    {
        var server = new PayServer();
        await using var phone = await DeviceAsync();
        var orderId = await AddOrderAsync(phone, 800m);
        await phone.GetRequiredService<IPaymentLocalService>().RecordAsync(Biz, orderId, Request(300m, PaymentMethod.MoMo));

        await Engine(phone, server).RunAsync(Biz);
        Assert.Equal([SyncEntityType.Customer, SyncEntityType.Order, SyncEntityType.Payment], server.PushLog);
        Assert.Empty(await QueueAsync(phone));
        Assert.False(Assert.Single((await phone.GetRequiredService<IPaymentLocalService>().GetForOrderAsync(Biz, orderId))!.Payments).IsPending);

        await using var tablet = await DeviceAsync();
        await Engine(tablet, server).RunAsync(Biz);
        var money = (await tablet.GetRequiredService<IPaymentLocalService>().GetForOrderAsync(Biz, orderId))!;
        Assert.Equal(500m, money.Summary.Balance);
        Assert.Empty(await QueueAsync(tablet)); // pulled payments are not re-queued
    }

    [Fact]
    public async Task Pull_PaymentBeforeItsOrder_IsDeferred_ThenApplied()
    {
        var t = DateTimeOffset.UtcNow;
        var customer = new SyncCustomerDto("C1", "Yaa", "+233245550123", null, null, t, t);
        var order = Order("O1", "C1", 800m, t);
        var payment = Payment("P1", "O1", "C1", 200m, t);
        var api = new Pages(
            Page(payments: [payment], hasMore: true),
            Page(customers: [customer], orders: [order]));
        await using var device = await DeviceAsync();
        var engine = Engine(device, api);

        await engine.PullAsync(Biz);

        var money = (await device.GetRequiredService<IPaymentLocalService>().GetForOrderAsync(Biz, "O1"))!;
        Assert.Equal(600m, money.Summary.Balance);
        await using var scope = device.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<LocalDbContext>().SyncDeferred.ToListAsync());
    }

    [Fact]
    public async Task Pull_ServerVoid_UpdatesLocalCopy_AndRaisesLiveRefresh()
    {
        var server = new PayServer();
        await using var device = await DeviceAsync();
        var orderId = await AddOrderAsync(device, 300m);
        var id = (await device.GetRequiredService<IPaymentLocalService>().RecordAsync(Biz, orderId, Request(300m, PaymentMethod.Cash))).Value!.Id;
        await Engine(device, server).RunAsync(Biz);

        server.Payments[id] = server.Payments[id] with { IsVoided = true, VoidedAtUtc = DateTimeOffset.UtcNow, DateModifiedUtc = DateTimeOffset.UtcNow };
        var notified = 0;
        void OnChanged(string _) => Interlocked.Increment(ref notified);
        LocalDataVersion.ChangedBySync += OnChanged;
        try
        {
            await Engine(device, server).RunAsync(Biz);
        }
        finally
        {
            LocalDataVersion.ChangedBySync -= OnChanged;
        }

        Assert.True(notified > 0);
        Assert.Equal(300m, (await device.GetRequiredService<IPaymentLocalService>().GetForOrderAsync(Biz, orderId))!.Summary.Balance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mismatch_NeverDiscardsSilently_KeepBothOrTakeTheirs(bool keepMine)
    {
        var server = new PayServer();
        await using var device = await DeviceAsync();
        var orderId = await AddOrderAsync(device, 800m);
        await Engine(device, server).RunAsync(Biz); // order on server
        var request = Request(200m, PaymentMethod.Cash);
        request.Id = IdGenerator.NewId();
        await device.GetRequiredService<IPaymentLocalService>().RecordAsync(Biz, orderId, request);
        // Another device already stored a different payment under the same id (bad clone/restore).
        var t = DateTimeOffset.UtcNow;
        server.Payments[request.Id] = Payment(request.Id, orderId, server.Orders[orderId].CustomerId, 150m, t);

        var engine = Engine(device, server);
        await engine.RunAsync(Biz);
        var conflict = Assert.Single(await QueueAsync(device), q => q.State == SyncQueueState.Conflict);
        Assert.Equal(SyncErrorCodes.PaymentMismatch, conflict.ConflictCode);
        Assert.Contains("will not discard a payment", conflict.LastError);
        Assert.Equal(200m, (await device.GetRequiredService<IPaymentLocalService>().GetForOrderAsync(Biz, orderId))!.Summary.PaidAmount);

        Assert.True(await engine.ResolveConflictAsync(Biz, conflict.Id, keepMine));
        await engine.RunAsync(Biz);

        Assert.Empty(await QueueAsync(device));
        var amounts = (await device.GetRequiredService<IPaymentLocalService>().GetForOrderAsync(Biz, orderId))!.Payments
            .Select(p => p.Payment.Amount).OrderBy(a => a).ToArray();
        Assert.Equal(keepMine ? [150m, 200m] : [150m], amounts);
        Assert.Equal(amounts, server.Payments.Values.Select(p => p.Amount).OrderBy(a => a).ToArray());
    }

    [Fact]
    public async Task OrderDeletedElsewhere_WithUnsyncedPayment_KeepsOrder_KeepMineRecreatesBoth()
    {
        var server = new PayServer();
        await using var device = await DeviceAsync();
        var orderId = await AddOrderAsync(device, 800m);
        await Engine(device, server).RunAsync(Biz);
        await device.GetRequiredService<IPaymentLocalService>().RecordAsync(Biz, orderId, Request(100m, PaymentMethod.Cash));
        server.Orders.Remove(orderId);
        server.Deleted.Add(new SyncDeletedItemDto(SyncEntityType.Order, orderId, DateTimeOffset.UtcNow.AddSeconds(5)));
        server.HoldPushes = true; // the delete arrives before this phone pushes

        var engine = Engine(device, server);
        await engine.PullAsync(Biz);
        var orderConflict = Assert.Single(await QueueAsync(device), q => q.EntityType == SyncEntityType.Order);
        Assert.Equal(SyncQueueState.Conflict, orderConflict.State);
        Assert.Contains("payments recorded on this device", orderConflict.LastError);
        Assert.NotNull(await device.GetRequiredService<IOrderLocalService>().GetByIdAsync(Biz, orderId));

        server.HoldPushes = false;
        Assert.True(await engine.ResolveConflictAsync(Biz, orderConflict.Id, keepMine: true));
        await engine.RunAsync(Biz);
        Assert.Empty(await QueueAsync(device));
        Assert.True(server.Orders.ContainsKey(orderId));
        Assert.Equal(100m, Assert.Single(server.Payments.Values).Amount);
    }

    [Fact]
    public async Task PushedPayment_ForOrderGoneOnServer_IsParentDeleted_KeepMineRestoresOrder()
    {
        var server = new PayServer();
        await using var device = await DeviceAsync();
        var orderId = await AddOrderAsync(device, 800m);
        await Engine(device, server).RunAsync(Biz);
        await device.GetRequiredService<IPaymentLocalService>().RecordAsync(Biz, orderId, Request(100m, PaymentMethod.Cash));
        server.Orders.Remove(orderId); // gone on the server, tombstone not pulled yet

        var engine = Engine(device, server);
        await engine.PushAsync(Biz);
        var conflict = Assert.Single(await QueueAsync(device));
        Assert.Equal(SyncErrorCodes.ParentDeleted, conflict.ConflictCode);
        Assert.Equal("Its order was deleted on another device.", conflict.LastError);

        Assert.True(await engine.ResolveConflictAsync(Biz, conflict.Id, keepMine: true));
        await engine.PushAsync(Biz);
        Assert.Empty(await QueueAsync(device));
        Assert.True(server.Orders.ContainsKey(orderId));
        Assert.Single(server.Payments.Values);
    }

    [Fact]
    public async Task Resync_KeepsOrdersThatHoldUnsyncedPayments_AndAdoptsThem()
    {
        var server = new PayServer();
        await using var device = await DeviceAsync();
        var orderId = await AddOrderAsync(device, 800m);
        await Engine(device, server).RunAsync(Biz);
        var paymentId = (await device.GetRequiredService<IPaymentLocalService>().RecordAsync(Biz, orderId, Request(100m, PaymentMethod.Cash))).Value!.Id;

        // Simulate an older build: the payment row exists but its queue item doesn't.
        await using (var scope = device.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            await db.SyncQueue.Where(q => q.EntityType == SyncEntityType.Payment).ExecuteDeleteAsync();
        }

        await Engine(device, server).ResyncAsync(Biz);
        await Engine(device, server).PushAsync(Biz);
        Assert.True(server.Payments.ContainsKey(paymentId));
        Assert.NotNull(await device.GetRequiredService<IOrderLocalService>().GetByIdAsync(Biz, orderId));
    }

    // ------------------------------------------------------------------ UI helpers

    [Theory]
    [InlineData("300", 300)]
    [InlineData("300.5", 300.5)]
    [InlineData("1,200.00", 1200)]
    [InlineData("GH₵ 75", 75)]
    [InlineData(" 0.01 ", 0.01)]
    public void TryParseAmount_AcceptsCediInput(string text, double expected) =>
        Assert.True(RecordPaymentViewModel.TryParseAmount(text, out var amount) && amount == (decimal)expected);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    public void TryParseAmount_RejectsJunk(string text) => Assert.False(RecordPaymentViewModel.TryParseAmount(text, out _));

    [Fact]
    public async Task Receipt_HasHeaderTotalsMethod_AndOfflineCopy()
    {
        await using var device = await DeviceAsync();
        var orderId = await AddOrderAsync(device, 800m);
        var payments = device.GetRequiredService<IPaymentLocalService>();
        var id = (await payments.RecordAsync(Biz, orderId, Request(300m, PaymentMethod.MoMo))).Value!.Id;

        var receipt = (await payments.GetReceiptAsync(Biz, id))!;
        var text = receipt.ToShareText("Ama Stitches");
        Assert.StartsWith("Ama Stitches\nPayment receipt", text);
        Assert.Contains("Amount: GH₵ 300.00", text);
        Assert.Contains("Method: Mobile Money", text);
        Assert.Contains("Order total: GH₵ 800.00", text);
        Assert.Contains("Balance: GH₵ 500.00", text);
        Assert.Contains("Recorded on this device; not yet synced.", text);
        Assert.DoesNotContain("confirmed", text, StringComparison.OrdinalIgnoreCase); // never looks provider-confirmed
    }

    [Fact]
    public async Task OrderDetail_HidesPayments_FromTailor()
    {
        await using var device = await DeviceAsync();
        var orderId = await AddOrderAsync(device, 800m);
        await device.GetRequiredService<IPaymentLocalService>().RecordAsync(Biz, orderId, Request(300m, PaymentMethod.Cash));

        var vm = new OrderDetailViewModel(device.GetRequiredService<IOrderLocalService>(), new RoleAuth(BusinessRole.Tailor),
            device.GetRequiredService<IPaymentLocalService>());
        vm.ApplyQueryAttributesForTest(orderId);
        await vm.RefreshPaymentsAsync();
        Assert.False(vm.ShowPayments);
        Assert.Empty(vm.Payments);

        var owner = new OrderDetailViewModel(device.GetRequiredService<IOrderLocalService>(), new RoleAuth(BusinessRole.Owner),
            device.GetRequiredService<IPaymentLocalService>());
        owner.ApplyQueryAttributesForTest(orderId);
        await owner.RefreshPaymentsAsync();
        Assert.True(owner.ShowPayments);
        Assert.True(owner.CanRecordPayment);
        Assert.Equal("GH₵ 500.00 left", owner.BalanceText);
        Assert.Single(owner.Payments);
    }

    [Fact]
    public void Migrator_LatestVersion_IsPayments() => Assert.Equal(8, LocalSchemaMigrator.LatestVersion);

    // ------------------------------------------------------------------ helpers

    private static RecordPaymentRequest Request(decimal amount, string method) => new() { Amount = amount, Method = method };

    private static SyncOrderDto Order(string id, string customerId, decimal total, DateTimeOffset t) =>
        new(id, customerId, "0042", OrderStatus.New, null, null, null, total, null, t, t,
            [new SyncOrderLineDto(id + "-L", GarmentTemplate.Shirt, 1, total, null, MeasurementUnit.Centimetres, "{}", null, 0)]);

    private static SyncPaymentDto Payment(string id, string orderId, string customerId, decimal amount, DateTimeOffset t) =>
        new(id, orderId, customerId, amount, PaymentMethod.Cash, null, t, false, null, null, t, t);

    private static SyncPullResponse Page(
        SyncCustomerDto[]? customers = null, SyncOrderDto[]? orders = null, SyncPaymentDto[]? payments = null, bool hasMore = false) =>
        new(customers ?? [], [], orders ?? [], [], "c", hasMore, DateTimeOffset.UtcNow) { Payments = payments ?? [] };

    private static SyncEngine Engine(ServiceProvider device, ISyncApi api) => new(device.GetRequiredService<IServiceScopeFactory>(), api);

    private static async Task<string> AddOrderAsync(ServiceProvider device, decimal total)
    {
        var (c, error) = await device.GetRequiredService<ICustomerLocalService>()
            .SaveAsync(Biz, new CreateCustomerRequest("Efua Mensah", "0245550123", null, null));
        Assert.Null(error);
        var (o, orderError) = await device.GetRequiredService<IOrderLocalService>().SaveAsync(Biz, new CreateOrderRequest
        {
            CustomerId = c!.Id,
            TotalAmount = total,
            Lines =
            [
                new CreateOrderLineRequest
                {
                    GarmentTemplate = GarmentTemplate.Dress, Quantity = 1, UnitPrice = total,
                    Unit = MeasurementUnit.Centimetres, MeasurementFields = new Dictionary<string, decimal?>()
                }
            ]
        });
        Assert.Null(orderError);
        return o!.Id;
    }

    private static async Task<List<SyncQueueItem>> QueueAsync(ServiceProvider device)
    {
        await using var scope = device.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LocalDbContext>().SyncQueue.AsNoTracking().ToListAsync();
    }

    private static async Task<ServiceProvider> DeviceAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"needlebase-pay-{Guid.NewGuid():N}.db3");
        var services = new ServiceCollection();
        services.AddDbContext<LocalDbContext>(o => o.UseSqlite($"Data Source={path};Pooling=False"));
        services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
        services.AddSingleton<IOrderLocalService, OrderLocalService>();
        services.AddSingleton<IPaymentLocalService, PaymentLocalService>();
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await LocalSchemaMigrator.MigrateAsync(scope.ServiceProvider.GetRequiredService<LocalDbContext>());
        return provider;
    }

    private sealed class RoleAuth(string role) : NeedleBase.Mobile.Services.IAuthService
    {
        public Task<NeedleBase.Application.Contracts.Auth.AuthUserDto?> RegisterBusinessAsync(NeedleBase.Application.Contracts.Auth.RegisterBusinessRequest request, CancellationToken cancellationToken = default) => Task.FromResult<NeedleBase.Application.Contracts.Auth.AuthUserDto?>(null);
        public Task<NeedleBase.Application.Contracts.Auth.AuthUserDto?> LoginAsync(NeedleBase.Application.Contracts.Auth.LoginRequest request, CancellationToken cancellationToken = default) => Task.FromResult<NeedleBase.Application.Contracts.Auth.AuthUserDto?>(null);
        public Task<NeedleBase.Mobile.Services.TokenRefreshResult> RefreshAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasStoredTokensAsync() => Task.FromResult(true);
        public Task ClearTokensAsync() => Task.CompletedTask;
        public Task<bool> LogoutAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("t");
        public Task StoreTokensAsync(NeedleBase.Application.Contracts.Auth.TokenResponse tokens) => Task.CompletedTask;
        public Task StoreSessionAsync(NeedleBase.Application.Contracts.Auth.AuthUserDto user) => Task.CompletedTask;
        public Task<string?> GetBusinessIdAsync() => Task.FromResult<string?>(Biz);
        public Task<NeedleBase.Application.Contracts.Auth.AuthUserDto?> GetCachedUserAsync() =>
            Task.FromResult<NeedleBase.Application.Contracts.Auth.AuthUserDto?>(new("u1", "owner@ama-stitches.test", "Ama", "Owusu", Biz, "Ama Stitches", role));
    }

    private sealed class Pages(params SyncPullResponse[] pages) : ISyncApi
    {
        private int _i;

        public Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new SyncPushResponse([]));

        public Task<SyncPullResponse> PullAsync(SyncPullRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(pages[Math.Min(_i++, pages.Length - 1)]);
    }

    /// <summary>In-memory server with the API's payment rules (immutable, one-way void, mismatch = conflict, lenient create).</summary>
    private sealed class PayServer : ISyncApi
    {
        private long _tick;
        private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;

        public Dictionary<string, SyncCustomerDto> Customers { get; } = new();
        public Dictionary<string, SyncOrderDto> Orders { get; } = new();
        public Dictionary<string, SyncPaymentDto> Payments { get; } = new();
        public List<SyncDeletedItemDto> Deleted { get; } = new();
        public List<string> PushLog { get; } = new();
        public bool HoldPushes { get; set; }

        private DateTimeOffset Now() => _start.AddMilliseconds(++_tick);

        public Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken)
        {
            if (HoldPushes) throw new SyncTransientException("offline");
            var results = new List<SyncPushResult>();
            foreach (var op in request.Operations)
            {
                PushLog.Add(op.EntityType);
                results.Add(Apply(op));
            }

            return Task.FromResult(new SyncPushResponse(results));
        }

        private SyncPushResult Apply(SyncPushOperation op)
        {
            switch (op.EntityType)
            {
                case SyncEntityType.Customer:
                {
                    Customers.TryGetValue(op.EntityId, out var existing);
                    var t = Now();
                    var saved = op.Customer! with { CreatedAtUtc = existing?.CreatedAtUtc ?? t, DateModifiedUtc = t };
                    Customers[op.EntityId] = saved;
                    return new(op.OpId, SyncOpStatus.Applied, null, null, saved);
                }

                case SyncEntityType.Order:
                {
                    if (!Customers.ContainsKey(op.Order!.CustomerId))
                        return new(op.OpId, SyncOpStatus.Conflict, SyncErrorCodes.ParentDeleted, "x");
                    var t = Now();
                    var saved = op.Order with { DateModifiedUtc = t };
                    Orders[op.EntityId] = saved;
                    Deleted.RemoveAll(d => d.EntityId == op.EntityId);
                    return new(op.OpId, SyncOpStatus.Applied, null, null, Order: saved);
                }

                default:
                {
                    var dto = op.Payment!;
                    if (Payments.TryGetValue(op.EntityId, out var existing))
                    {
                        if (existing.Amount != dto.Amount || existing.Method != dto.Method || existing.OrderId != dto.OrderId)
                            return new(op.OpId, SyncOpStatus.Conflict, SyncErrorCodes.PaymentMismatch, "x", Payment: existing);
                        if (dto.IsVoided && !existing.IsVoided)
                            Payments[op.EntityId] = existing = existing with
                            {
                                IsVoided = true, VoidedAtUtc = dto.VoidedAtUtc, VoidReason = dto.VoidReason, DateModifiedUtc = Now()
                            };
                        return new(op.OpId, SyncOpStatus.Applied, null, null, Payment: existing);
                    }

                    if (!Orders.ContainsKey(dto.OrderId))
                        return new(op.OpId, SyncOpStatus.Conflict, SyncErrorCodes.ParentDeleted, "x");
                    var saved = dto with { DateModifiedUtc = Now() };
                    Payments[op.EntityId] = saved;
                    return new(op.OpId, SyncOpStatus.Applied, null, null, Payment: saved);
                }
            }
        }

        public Task<SyncPullResponse> PullAsync(SyncPullRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new SyncPullResponse(Customers.Values.ToList(), [], Orders.Values.ToList(),
                request.Cursor is null ? [] : Deleted.ToList(), "c", false, DateTimeOffset.UtcNow) { Payments = Payments.Values.ToList() });
    }
}
