using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Licensing;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;

namespace NeedleBase.Mobile.Tests;

public class FreeModeLocalServiceTests
{
    [Fact]
    public async Task LifetimeCounters_DoNotDecreaseOnDelete()
    {
        await using var provider = BuildProvider();
        var free = provider.GetRequiredService<IFreeModeLocalService>();
        var customers = provider.GetRequiredService<ICustomerLocalService>();
        const string businessId = "01FREEMODETESTBIZ000000001";

        LocalFreeModeGate.Set(businessId);
        try
        {
            await free.CreateAsync(businessId, "Ama Stitches", DateTimeOffset.UtcNow);

            for (var i = 0; i < FreePlanLimits.MaxCustomersEver; i++)
            {
                var phone = $"+23324555{i:D4}";
                var result = await customers.SaveAsync(businessId, new CreateCustomerRequest($"C{i}", phone, null, null));
                Assert.True(result.IsSuccess, result.Message);
            }

            var blocked = await customers.SaveAsync(
                businessId, new CreateCustomerRequest("Overflow", "+233245559999", null, null));
            Assert.Equal(RecordError.FreeCustomerLimit, blocked.Error);

            var list = await customers.SearchAsync(businessId, null, take: 50);
            Assert.Equal(FreePlanLimits.MaxCustomersEver, list.Count);
            Assert.Equal(CustomerDeleteResult.Deleted, await customers.DeleteAsync(businessId, list[0].Id));

            var stillBlocked = await customers.SaveAsync(
                businessId, new CreateCustomerRequest("AfterDelete", "+233245558888", null, null));
            Assert.Equal(RecordError.FreeCustomerLimit, stillBlocked.Error);

            var (used, _) = await free.GetUsageAsync(businessId);
            Assert.Equal(FreePlanLimits.MaxCustomersEver, used);
        }
        finally
        {
            LocalFreeModeGate.Clear();
        }
    }

    [Fact]
    public async Task OrderCap_BlocksAfterLifetimeLimit()
    {
        await using var provider = BuildProvider();
        var free = provider.GetRequiredService<IFreeModeLocalService>();
        var customers = provider.GetRequiredService<ICustomerLocalService>();
        var orders = provider.GetRequiredService<IOrderLocalService>();
        const string businessId = "01FREEMODETESTBIZ000000002";

        LocalFreeModeGate.Set(businessId);
        try
        {
            await free.CreateAsync(businessId, "Kofi Tailoring", DateTimeOffset.UtcNow);
            var (customer, _) = await customers.SaveAsync(
                businessId, new CreateCustomerRequest("Efua", "+233245550001", null, null));
            Assert.NotNull(customer);

            for (var i = 0; i < FreePlanLimits.MaxOrdersEver; i++)
            {
                var result = await orders.SaveAsync(businessId, NewOrder(customer!.Id));
                Assert.True(result.IsSuccess, result.Message);
            }

            var blocked = await orders.SaveAsync(businessId, NewOrder(customer!.Id));
            Assert.Equal(RecordError.FreeOrderLimit, blocked.Error);
        }
        finally
        {
            LocalFreeModeGate.Clear();
        }
    }

    [Fact]
    public async Task SyncQueue_NotWritten_WhenFreeGateActive()
    {
        await using var provider = BuildProvider();
        var free = provider.GetRequiredService<IFreeModeLocalService>();
        var customers = provider.GetRequiredService<ICustomerLocalService>();
        const string businessId = "01FREEMODETESTBIZ000000003";

        LocalFreeModeGate.Set(businessId);
        try
        {
            await free.CreateAsync(businessId, "Free Shop", DateTimeOffset.UtcNow);
            var result = await customers.SaveAsync(
                businessId, new CreateCustomerRequest("Ama", "+233245550123", null, null));
            Assert.True(result.IsSuccess);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            Assert.Empty(await db.SyncQueue.AsNoTracking().ToListAsync());
        }
        finally
        {
            LocalFreeModeGate.Clear();
        }
    }

    [Fact]
    public async Task MissingFreeState_FailClosed_WhenGateActive()
    {
        await using var provider = BuildProvider();
        var customers = provider.GetRequiredService<ICustomerLocalService>();
        const string businessId = "01FREEMODETESTBIZ000000004";

        LocalFreeModeGate.Set(businessId);
        try
        {
            // No CreateAsync — caps must not fail open.
            var blocked = await customers.SaveAsync(
                businessId, new CreateCustomerRequest("Ghost", "+233245550777", null, null));
            Assert.Equal(RecordError.FreeCustomerLimit, blocked.Error);
        }
        finally
        {
            LocalFreeModeGate.Clear();
        }
    }

    [Fact]
    public async Task FailedWipe_KeepsFreeGateActive()
    {
        LocalFreeModeGate.Set("01FREEMODETESTBIZ000000005");
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(new LocalDbOptions { DbPath = "/no/such/dir/needlebase-wipe-fail.db3" });
            services.AddDbContext<LocalDbContext>(o =>
                o.UseSqlite("Data Source=/no/such/dir/needlebase-wipe-fail.db3"));
            await using var provider = services.BuildServiceProvider();
            var db = provider.GetRequiredService<IServiceScopeFactory>();
            var local = new LocalDatabaseService(db, provider.GetRequiredService<LocalDbOptions>());

            Assert.True(LocalFreeModeGate.IsActive);
            Assert.False(await local.WipeAsync());
            Assert.True(LocalFreeModeGate.IsActive);
            Assert.Equal("01FREEMODETESTBIZ000000005", LocalFreeModeGate.BusinessId);
        }
        finally
        {
            LocalFreeModeGate.Clear();
        }
    }

    [Fact]
    public async Task ReserveThenRelease_AllowsRetryAfterFailedCreate()
    {
        await using var provider = BuildProvider();
        var free = provider.GetRequiredService<IFreeModeLocalService>();
        const string businessId = "01FREEMODETESTBIZ000000006";

        LocalFreeModeGate.Set(businessId);
        try
        {
            await free.CreateAsync(businessId, "Retry Shop", DateTimeOffset.UtcNow);
            Assert.Null(await free.TryReserveCustomerSlotAsync(businessId));
            var (usedAfterReserve, _) = await free.GetUsageAsync(businessId);
            Assert.Equal(1, usedAfterReserve);

            await free.ReleaseCustomerSlotAsync(businessId);
            var (usedAfterRelease, _) = await free.GetUsageAsync(businessId);
            Assert.Equal(0, usedAfterRelease);
        }
        finally
        {
            LocalFreeModeGate.Clear();
        }
    }

    private static CreateOrderRequest NewOrder(string customerId) => new()
    {
        CustomerId = customerId,
        TotalAmount = 100m,
        Lines =
        [
            new CreateOrderLineRequest
            {
                GarmentTemplate = GarmentTemplate.Dress,
                Quantity = 1,
                UnitPrice = 100m,
                Unit = MeasurementUnit.Centimetres
            }
        ]
    };

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        var dbPath = Path.Combine(Path.GetTempPath(), $"needlebase-free-{Guid.NewGuid():N}.db3");
        services.AddSingleton(new LocalDbOptions { DbPath = dbPath });
        services.AddDbContext<LocalDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath};Cache=Shared"));
        services.AddSingleton<IFreeModeLocalService, FreeModeLocalService>();
        services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
        services.AddSingleton<IOrderLocalService, OrderLocalService>();
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            LocalSchemaMigrator.MigrateAsync(db).GetAwaiter().GetResult();
        }

        return provider;
    }
}
