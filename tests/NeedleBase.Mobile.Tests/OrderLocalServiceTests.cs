using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;

namespace NeedleBase.Mobile.Tests;

public class OrderLocalServiceTests
{
    [Fact]
    public async Task SaveOrder_SnapshotsMeasurements_AndUpdatesStatus()
    {
        var path = TempDb();
        await using var provider = BuildProvider(path);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            await LocalSchemaMigrator.MigrateAsync(db);
        }

        var customers = provider.GetRequiredService<ICustomerLocalService>();
        var measurements = provider.GetRequiredService<IMeasurementLocalService>();
        var orders = provider.GetRequiredService<IOrderLocalService>();
        const string biz = "biz-orders-1";

        var (customer, _) = await customers.SaveAsync(
            biz,
            new CreateCustomerRequest("Efua Mensah", "+233245550123", null, null));
        Assert.NotNull(customer);

        var (measurement, _) = await measurements.SaveAsync(
            biz,
            customer!.Id,
            new CreateMeasurementRequest
            {
                GarmentTemplate = GarmentTemplate.Dress,
                Unit = MeasurementUnit.Centimetres,
                Fields = new Dictionary<string, decimal?> { ["bust"] = 92m, ["waist"] = 70m }
            });
        Assert.NotNull(measurement);

        var (order, error) = await orders.SaveAsync(
            biz,
            new CreateOrderRequest
            {
                CustomerId = customer.Id,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                TotalAmount = 800m,
                Lines =
                [
                    new CreateOrderLineRequest
                    {
                        GarmentTemplate = GarmentTemplate.Dress,
                        Quantity = 1,
                        UnitPrice = 800m,
                        MeasurementProfileId = measurement!.Id,
                        Unit = MeasurementUnit.Centimetres,
                        MeasurementFields = new Dictionary<string, decimal?>()
                    }
                ]
            });

        Assert.Null(error);
        Assert.NotNull(order);
        Assert.Equal(OrderStatus.New, order!.Status);
        Assert.True(order.IsOverdue);
        Assert.Equal(92m, order.Lines[0].MeasurementFields["bust"]);

        var (updated, statusError) = await orders.UpdateStatusAsync(biz, order.Id, OrderStatus.Ready);
        Assert.Null(statusError);
        Assert.Equal(OrderStatus.Ready, updated!.Status);
        Assert.Equal("Ready to collect", updated.StatusDisplay);

        var counts = await orders.GetCountsAsync(biz);
        Assert.Equal(1, counts.ActiveCount);
        Assert.Equal(1, counts.ReadyToCollectCount);

        var filtered = await orders.SearchAsync(biz, search: "Efua", status: OrderStatus.Ready);
        Assert.Contains(filtered, o => o.Id == order.Id);

        var none = await orders.SearchAsync(biz, search: null, status: OrderStatus.New);
        Assert.DoesNotContain(none, o => o.Id == order.Id);
    }



    [Fact]
    public async Task Errors_AreFriendly_DeleteGuard_AndSearchIgnoresCase()
    {
        var path = TempDb();
        await using var provider = BuildProvider(path);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            await LocalSchemaMigrator.MigrateAsync(db);
        }

        var customers = provider.GetRequiredService<ICustomerLocalService>();
        var orders = provider.GetRequiredService<IOrderLocalService>();
        const string biz = "biz-orders-2";

        var (customer, _) = await customers.SaveAsync(
            biz,
            new CreateCustomerRequest("Akosua Boateng", "+233245550124", null, null));
        var (spare, _) = await customers.SaveAsync(
            biz,
            new CreateCustomerRequest("Adwoa", "+233245550125", null, null));

        CreateOrderRequest Request(decimal? total) => new()
        {
            CustomerId = customer!.Id,
            TotalAmount = total,
            Lines =
            [
                new CreateOrderLineRequest
                {
                    GarmentTemplate = GarmentTemplate.Shirt,
                    Quantity = 2,
                    UnitPrice = 50m,
                    Unit = MeasurementUnit.Centimetres,
                    MeasurementFields = new Dictionary<string, decimal?>()
                }
            ]
        };

        var (_, mismatch) = await orders.SaveAsync(biz, Request(1m));
        Assert.Equal(OrderErrorMessages.For(OrderErrorMessages.TotalMismatch), mismatch);

        var (order, error) = await orders.SaveAsync(biz, Request(null));
        Assert.Null(error);
        Assert.Equal(100m, order!.TotalAmount);
        Assert.Equal(8, order.DisplayRef.Length);

        var (_, statusError) = await orders.UpdateStatusAsync(biz, order.Id, "Teleported");
        Assert.Equal(OrderErrorMessages.For(OrderErrorMessages.OrderStatusInvalid), statusError);
        Assert.DoesNotContain("Unknown", statusError);

        await orders.UpdateStatusAsync(biz, order.Id, OrderStatus.Collected);
        var (_, lockedError) = await orders.UpdateStatusAsync(biz, order.Id, OrderStatus.Sewing);
        Assert.Equal(OrderErrorMessages.For(OrderErrorMessages.OrderLocked), lockedError);

        Assert.Contains(await orders.SearchAsync(biz, search: "akosua", status: null), o => o.Id == order.Id);
        Assert.Contains(
            await orders.SearchAsync(biz, search: "#" + order.DisplayRef.ToLowerInvariant(), status: null),
            o => o.Id == order.Id);
        Assert.Contains(await customers.SearchAsync(biz, "BOATENG"), c => c.Id == customer!.Id);

        Assert.Equal(CustomerDeleteResult.HasOrders, await customers.DeleteAsync(biz, customer!.Id));
        Assert.Equal(CustomerDeleteResult.Deleted, await customers.DeleteAsync(biz, spare!.Id));
        Assert.Equal(CustomerDeleteResult.NotFound, await customers.DeleteAsync(biz, spare.Id));
    }

    private static ServiceProvider BuildProvider(string path)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LocalDbContext>(o =>
            o.UseSqlite($"Data Source={path};Pooling=False"));
        services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
        services.AddSingleton<IMeasurementLocalService, MeasurementLocalService>();
        services.AddSingleton<IOrderLocalService, OrderLocalService>();
        return services.BuildServiceProvider();
    }

    private static string TempDb() =>
        Path.Combine(Path.GetTempPath(), $"needlebase-order-{Guid.NewGuid():N}.db3");
}
