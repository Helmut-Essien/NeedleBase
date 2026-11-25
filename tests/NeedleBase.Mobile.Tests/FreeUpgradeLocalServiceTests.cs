using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;

namespace NeedleBase.Mobile.Tests;

[Collection("LocalFreeModeGate")]
public class FreeUpgradeLocalServiceTests
{
    [Fact]
    public async Task RemapBusinessId_MovesShopRows_AndDropsFreeState()
    {
        await using var provider = BuildProvider();
        var free = provider.GetRequiredService<IFreeModeLocalService>();
        var upgrade = provider.GetRequiredService<IFreeUpgradeLocalService>();
        var customers = provider.GetRequiredService<ICustomerLocalService>();
        var orders = provider.GetRequiredService<IOrderLocalService>();
        const string from = "01FREEUPGRADETESTFROM000001";
        const string to = "01FREEUPGRADETESTTO00000002";

        LocalFreeModeGate.Set(from);
        try
        {
            await free.CreateAsync(from, "Ama Stitches", DateTimeOffset.UtcNow);
            var (customer, _) = await customers.SaveAsync(from, new CreateCustomerRequest("Efua", "+233245550001", null, null));
            Assert.NotNull(customer);
            Assert.True((await orders.SaveAsync(from, NewOrder(customer!.Id))).IsSuccess);

            await upgrade.RemapBusinessIdAsync(from, to);

            Assert.False(await upgrade.HasShopRowsAsync(from));
            Assert.True(await upgrade.HasShopRowsAsync(to));
            Assert.NotNull(await customers.GetByIdAsync(to, customer.Id));
            Assert.False(await free.ExistsAsync(from));
            Assert.False(await free.ExistsAsync(to));
        }
        finally
        {
            LocalFreeModeGate.Clear();
        }
    }

    [Fact]
    public async Task RemapBusinessId_IsIdempotent()
    {
        await using var provider = BuildProvider();
        var free = provider.GetRequiredService<IFreeModeLocalService>();
        var upgrade = provider.GetRequiredService<IFreeUpgradeLocalService>();
        var customers = provider.GetRequiredService<ICustomerLocalService>();
        const string from = "01FREEUPGRADETESTFROM000003";
        const string to = "01FREEUPGRADETESTTO00000004";

        LocalFreeModeGate.Set(from);
        try
        {
            await free.CreateAsync(from, "Shop", DateTimeOffset.UtcNow);
            var (customer, _) = await customers.SaveAsync(from, new CreateCustomerRequest("Kofi", "+233245550002", null, null));
            await upgrade.RemapBusinessIdAsync(from, to);
            await upgrade.RemapBusinessIdAsync(from, to);
            Assert.NotNull(await customers.GetByIdAsync(to, customer!.Id));
            Assert.False(await upgrade.HasShopRowsAsync(from));
        }
        finally
        {
            LocalFreeModeGate.Clear();
        }
    }

    private static CreateOrderRequest NewOrder(string customerId) => new()
    {
        CustomerId = customerId,
        TotalAmount = 50m,
        Lines =
        [
            new CreateOrderLineRequest
            {
                GarmentTemplate = GarmentTemplate.Dress,
                Quantity = 1,
                UnitPrice = 50m,
                Unit = MeasurementUnit.Centimetres
            }
        ]
    };

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        var dbPath = Path.Combine(Path.GetTempPath(), $"needlebase-upgrade-{Guid.NewGuid():N}.db3");
        services.AddSingleton(new LocalDbOptions { DbPath = dbPath });
        services.AddDbContext<LocalDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath};Cache=Shared"));
        services.AddSingleton<IFreeModeLocalService, FreeModeLocalService>();
        services.AddSingleton<IFreeUpgradeLocalService, FreeUpgradeLocalService>();
        services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
        services.AddSingleton<IOrderLocalService, OrderLocalService>();
        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            LocalSchemaMigrator.MigrateAsync(scope.ServiceProvider.GetRequiredService<LocalDbContext>())
                .GetAwaiter().GetResult();
        }

        return provider;
    }
}
