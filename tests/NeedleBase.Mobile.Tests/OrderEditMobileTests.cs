using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

/// <summary>Order editing on the phone: local rules, queueing, migrator v9 and the edit screen.</summary>
public partial class OrderEditMobileTests
{
    private const string Biz = "biz-edit";

    private static async Task<ServiceProvider> DeviceAsync(Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? spy = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"needlebase-edit-{Guid.NewGuid():N}.db3");
        var services = new ServiceCollection();
        services.AddDbContext<LocalDbContext>(o =>
        {
            o.UseSqlite($"Data Source={path};Pooling=False");
            if (spy is not null)
            {
                o.AddInterceptors(spy);
            }
        });
        services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
        services.AddSingleton<IMeasurementLocalService, MeasurementLocalService>();
        services.AddSingleton<IOrderLocalService, OrderLocalService>();
        services.AddSingleton<IPaymentLocalService, PaymentLocalService>();
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await LocalSchemaMigrator.MigrateAsync(scope.ServiceProvider.GetRequiredService<LocalDbContext>());
        return provider;
    }

    private static async Task<(OrderDetailDto Order, string CustomerId)> SeedAsync(ServiceProvider device, bool measured = true)
    {
        var (customer, _) = await device.GetRequiredService<ICustomerLocalService>().SaveAsync(Biz, new CreateCustomerRequest("Efua Mensah", "+233245550123", null, null));
        string? profileId = null;
        if (measured)
        {
            var (profile, _) = await device.GetRequiredService<IMeasurementLocalService>().SaveAsync(Biz, customer!.Id, new CreateMeasurementRequest
            {
                GarmentTemplate = GarmentTemplate.Dress,
                Unit = MeasurementUnit.Centimetres,
                Fields = new Dictionary<string, decimal?> { ["bust"] = 92m }
            });
            profileId = profile!.Id;
        }

        var (order, error) = await device.GetRequiredService<IOrderLocalService>().SaveAsync(Biz, new CreateOrderRequest
        {
            CustomerId = customer!.Id,
            TotalAmount = 500m,
            Lines = [new CreateOrderLineRequest { GarmentTemplate = GarmentTemplate.Dress, Quantity = 1, UnitPrice = 500m, MeasurementProfileId = profileId, Unit = MeasurementUnit.Centimetres, MeasurementFields = new Dictionary<string, decimal?>() }]
        });
        Assert.Null(error);
        return (order!, customer.Id);
    }

    private static UpdateOrderRequest Edit(OrderDetailDto o, decimal price, int qty = 1) => new()
    {
        BaseModifiedUtc = o.DateModifiedUtc,
        Notes = "hem",
        Lines = [new CreateOrderLineRequest { Id = o.Lines[0].Id, GarmentTemplate = o.Lines[0].GarmentTemplate, Quantity = qty, UnitPrice = price, Unit = o.Lines[0].Unit }]
    };

    [Fact]
    public async Task Create_SnapshotsWhenMeasurementsWereTaken()
    {
        await using var device = await DeviceAsync();
        var (order, _) = await SeedAsync(device);
        Assert.NotNull(order.Lines[0].MeasurementTakenAtUtc);
        Assert.True(order.Lines[0].HasMeasurements);
    }

    [Fact]
    public async Task Owner_Edit_SavesKeepsSnapshot_AndQueuesOrder()
    {
        await using var device = await DeviceAsync();
        var (order, _) = await SeedAsync(device);
        var orders = device.GetRequiredService<IOrderLocalService>();

        var result = await orders.UpdateAsync(Biz, order.Id, Edit(order, 300m, 2), BusinessRole.Owner);
        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(600m, result.Value!.TotalAmount);
        Assert.Equal("hem", result.Value.Notes);
        Assert.Equal(order.Lines[0].MeasurementTakenAtUtc, result.Value.Lines[0].MeasurementTakenAtUtc);
        Assert.Equal(92m, result.Value.Lines[0].MeasurementFields["bust"]);

        await using var scope = device.CreateAsyncScope();
        var queue = await scope.ServiceProvider.GetRequiredService<LocalDbContext>().SyncQueue.AsNoTracking().ToListAsync();
        var item = Assert.Single(queue, q => q.EntityId == order.Id);
        Assert.Contains("600", item.PayloadJson);
    }

    [Fact]
    public async Task Tailor_IsForbidden_Locally()
    {
        await using var device = await DeviceAsync();
        var (order, _) = await SeedAsync(device);
        var result = await device.GetRequiredService<IOrderLocalService>().UpdateAsync(Biz, order.Id, Edit(order, 300m), BusinessRole.Tailor);
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Equal(500m, (await device.GetRequiredService<IOrderLocalService>().GetByIdAsync(Biz, order.Id))!.TotalAmount);
    }

    [Fact]
    public async Task Stale_And_Locked_And_BelowPaid_AreRefused()
    {
        await using var device = await DeviceAsync();
        var (order, _) = await SeedAsync(device);
        var orders = device.GetRequiredService<IOrderLocalService>();

        var stale = Edit(order, 300m);
        stale.BaseModifiedUtc = order.DateModifiedUtc.AddMinutes(-5);
        Assert.Equal(RecordError.Stale, (await orders.UpdateAsync(Biz, order.Id, stale, BusinessRole.Manager)).Error);

        var paid = await device.GetRequiredService<IPaymentLocalService>().RecordAsync(Biz, order.Id, new RecordPaymentRequest { Amount = 400m, Method = PaymentMethod.Cash });
        Assert.True(paid.IsSuccess, paid.Message);
        var current = (await orders.GetByIdAsync(Biz, order.Id))!;
        var below = await orders.UpdateAsync(Biz, order.Id, Edit(current, 300m), BusinessRole.Owner);
        Assert.False(below.IsSuccess);
        Assert.Contains("paid", below.Message, StringComparison.OrdinalIgnoreCase);

        current = (await orders.UpdateStatusAsync(Biz, order.Id, OrderStatus.Cancelled)).Value!;
        Assert.False((await orders.UpdateAsync(Biz, order.Id, Edit(current, 600m), BusinessRole.Owner)).IsSuccess);
    }

    [Fact]
    public async Task OldOrder_ShowsNoMeasurements()
    {
        await using var device = await DeviceAsync();
        var (order, _) = await SeedAsync(device, measured: false);
        Assert.True(order.Lines[0].HasNoMeasurements);
        Assert.Equal("No measurements saved with this order", order.Lines[0].MeasurementTakenDisplay);
    }

    [Fact]
    public async Task Migrator_V8Database_GainsTakenAtColumn_KeepingRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"needlebase-v8-{Guid.NewGuid():N}.db3");
        await using (var db = new LocalDbContext(new DbContextOptionsBuilder<LocalDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options))
        {
            await LocalSchemaMigrator.MigrateAsync(db);
        }

        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """ALTER TABLE "OrderLines" DROP COLUMN "MeasurementTakenAtUtc"; PRAGMA user_version = 8;""";
            cmd.ExecuteNonQuery();
        }

        await using (var db = new LocalDbContext(new DbContextOptionsBuilder<LocalDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options))
        {
            Assert.Equal(LocalSchemaMigrator.LatestVersion, await LocalSchemaMigrator.MigrateAsync(db));
            Assert.Equal(0, await db.OrderLines.CountAsync(l => l.MeasurementTakenAtUtc != null));
            // Re-running is a no-op.
            Assert.Equal(LocalSchemaMigrator.LatestVersion, await LocalSchemaMigrator.MigrateAsync(db));
        }
    }

    [Fact]
    public async Task EditScreen_LoadsForOwner_ValidatesAndKeepsOneLine()
    {
        await using var device = await DeviceAsync();
        var (order, _) = await SeedAsync(device);
        var vm = new EditOrderViewModel(device.GetRequiredService<IOrderLocalService>(), new Auth(BusinessRole.Owner));
        vm.ApplyQueryAttributesForTest(order.Id);
        await vm.LoadAsync();

        Assert.True(vm.IsEditable);
        var row = Assert.Single(vm.Lines);
        Assert.Contains("Measurements kept", row.MeasurementNote);
        Assert.Contains("500.00", vm.TotalText);

        vm.RemoveLineCommand.Execute(row);
        Assert.Single(vm.Lines);
        Assert.Contains("at least one garment", vm.ErrorMessage);

        row.PriceText = "abc";
        Assert.Null(vm.BuildRequest());
        Assert.Contains("enter a price", vm.ErrorMessage);

        Assert.Equal(10.50m, EditOrderViewModel.ParseMoney("10,50")); // decimal comma, never GH₵ 1,050
        Assert.Null(EditOrderViewModel.ParseMoney("1.000"));
        row.PriceText = "GH₵ 1,200";
        vm.AddLineCommand.Execute(null);
        Assert.Equal(2, vm.Lines.Count);
        vm.Lines[1].PriceText = "100";
        var request = vm.BuildRequest();
        Assert.NotNull(request);
        Assert.Equal(1200m, request!.Lines![0].UnitPrice);
        Assert.Null(request.Lines[1].Id);
        Assert.Contains("1,300.00", vm.TotalText);
    }

    [Fact]
    public async Task EditScreen_Tailor_IsNotEditable()
    {
        await using var device = await DeviceAsync();
        var (order, _) = await SeedAsync(device);
        var vm = new EditOrderViewModel(device.GetRequiredService<IOrderLocalService>(), new Auth(BusinessRole.Tailor));
        vm.ApplyQueryAttributesForTest(order.Id);
        await vm.LoadAsync();
        Assert.False(vm.IsEditable);
        Assert.True(vm.HasError);
    }

    [Fact]
    public async Task Pull_ReplacesEditedLines_WithSnapshotDate()
    {
        await using var device = await DeviceAsync();
        var (order, customerId) = await SeedAsync(device);
        await using (var scope = device.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            // As if already synced: the server knows every row and nothing is waiting to push.
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"Customers\" SET \"ServerKnown\" = 1; UPDATE \"MeasurementProfiles\" SET \"ServerKnown\" = 1; UPDATE \"Orders\" SET \"ServerKnown\" = 1; DELETE FROM \"SyncQueue\";");
        }

        var taken = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var line = order.Lines[0];
        var server = new SyncOrderDto(order.Id, customerId, order.DisplayRef, order.Status, null, null, null, 1150m, "edited elsewhere",
            order.CreatedAtUtc, order.DateModifiedUtc.AddMinutes(5),
            [
                new SyncOrderLineDto(line.Id, line.GarmentTemplate, 2, 500m, null, line.Unit, """{"bust":92}""", null, 0, taken),
                new SyncOrderLineDto("01SERVERLINE00000000000001", GarmentTemplate.Shirt, 1, 150m, null, MeasurementUnit.Centimetres, "{}", null, 1)
            ]);
        var api = new OnePull(new SyncPullResponse([new SyncCustomerDto(customerId, "Efua Mensah", "+233245550123", null, null, order.CreatedAtUtc, order.CreatedAtUtc)], [], [server], [], "c", false, DateTimeOffset.UtcNow));
        await new SyncEngine(device.GetRequiredService<IServiceScopeFactory>(), api).RunAsync(Biz);

        var local = (await device.GetRequiredService<IOrderLocalService>().GetByIdAsync(Biz, order.Id))!;
        Assert.Equal(1150m, local.TotalAmount);
        Assert.Equal(2, local.Lines.Count);
        Assert.Equal(2, local.Lines.Single(l => l.Id == line.Id).Quantity);
        Assert.Equal(taken, local.Lines.Single(l => l.Id == line.Id).MeasurementTakenAtUtc);
        Assert.True(local.Lines.Single(l => l.GarmentTemplate == GarmentTemplate.Shirt).HasNoMeasurements);
    }

    private sealed class OnePull(SyncPullResponse page) : ISyncApi
    {
        public Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken) => Task.FromResult(new SyncPushResponse([]));
        public Task<SyncPullResponse> PullAsync(SyncPullRequest request, CancellationToken cancellationToken) => Task.FromResult(page);
    }

    private sealed class Auth(string role) : IAuthService
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
        public Task<AuthUserDto?> GetCachedUserAsync() => Task.FromResult<AuthUserDto?>(new("u1", "owner@ama-stitches.test", "Ama", "Owusu", Biz, "Ama Stitches", role));
    }
}
