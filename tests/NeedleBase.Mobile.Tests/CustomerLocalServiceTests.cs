using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;

namespace NeedleBase.Mobile.Tests;

public class CustomerLocalServiceTests
{
    [Fact]
    public async Task SaveCustomerAndDressMeasurement_PersistsLocally()
    {
        await using var provider = BuildProvider();
        var customers = provider.GetRequiredService<ICustomerLocalService>();
        var measurements = provider.GetRequiredService<IMeasurementLocalService>();
        const string businessId = "01ARZ3NDEKTSV4RRFFQ69G5FAW";

        var (customer, error) = await customers.SaveAsync(
            businessId,
            new CreateCustomerRequest("Efua Mensah", "+233245550123", null, null));
        Assert.Null(error);
        Assert.NotNull(customer);
        Assert.Equal("Efua Mensah", customer.Name);

        var list = await customers.SearchAsync(businessId, "Efua");
        Assert.Contains(list, c => c.Id == customer.Id);

        var (measurement, measureError) = await measurements.SaveAsync(
            businessId,
            customer.Id,
            new CreateMeasurementRequest
            {
                GarmentTemplate = GarmentTemplate.Dress,
                Unit = MeasurementUnit.Centimetres,
                Fields = new Dictionary<string, decimal?> { ["bust"] = 92m, ["waist"] = 70m }
            });
        Assert.Null(measureError);
        Assert.NotNull(measurement);
        Assert.Equal(GarmentTemplate.Dress, measurement.GarmentTemplate);

        var history = await measurements.ListForCustomerAsync(businessId, customer.Id);
        Assert.Single(history);

        var originalMeasuredAt = measurement.MeasuredAtUtc;
        var (edited, editError) = await measurements.SaveAsync(
            businessId,
            customer.Id,
            new CreateMeasurementRequest
            {
                Id = measurement.Id,
                GarmentTemplate = GarmentTemplate.Dress,
                Unit = MeasurementUnit.Centimetres,
                Fields = new Dictionary<string, decimal?> { ["bust"] = 94m, ["waist"] = 72m }
            });
        Assert.Null(editError);
        Assert.NotNull(edited);
        Assert.Equal(originalMeasuredAt, edited.MeasuredAtUtc);
        Assert.Equal(94m, edited.Fields["bust"]);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        var dbPath = Path.Combine(Path.GetTempPath(), $"needlebase-test-{Guid.NewGuid():N}.db3");
        services.AddSingleton(new LocalDbOptions { DbPath = dbPath });
        services.AddDbContext<LocalDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath};Cache=Shared"));
        services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
        services.AddSingleton<IMeasurementLocalService, MeasurementLocalService>();
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            LocalSchemaMigrator.MigrateAsync(db).GetAwaiter().GetResult();
        }

        return provider;
    }
}
