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

    [Fact]
    public async Task MeasurementWithEighths_IsStoredAndReadBackExactly_InAnyCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR"); // decimal comma
        try
        {
            await using var provider = BuildProvider();
            var customers = provider.GetRequiredService<ICustomerLocalService>();
            var measurements = provider.GetRequiredService<IMeasurementLocalService>();
            const string businessId = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
            var (customer, _) = await customers.SaveAsync(businessId, new CreateCustomerRequest("Efua Mensah", "+233245550123", null, null));
            var (saved, error) = await measurements.SaveAsync(businessId, customer!.Id, new CreateMeasurementRequest
            {
                GarmentTemplate = GarmentTemplate.Shirt,
                Unit = MeasurementUnit.Inches,
                Fields = new Dictionary<string, decimal?> { ["chest"] = 40.125m, ["waist"] = 4.5m }
            });
            Assert.Null(error);
            var again = await measurements.GetByIdAsync(businessId, saved!.Id);
            Assert.Equal(40.125m, again!.Fields["chest"]);
            Assert.Equal("40.125", NeedleBase.Application.Common.DecimalInput.Format(again.Fields["chest"]!.Value));
            Assert.Equal("4.5", NeedleBase.Application.Common.DecimalInput.Format(again.Fields["waist"]!.Value));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void UnitConversion_RoundedToThreeDecimals_KeepsEighthsOfAnInch()
    {
        // 10.16 cm = 4 in exactly; 4.125 in = 10.4775 cm -> shown 10.478, back to inches 4.125 (no drift).
        var cm = decimal.Round(4.125m * 2.54m, 3);
        Assert.Equal(10.478m, cm);
        Assert.Equal(4.125m, decimal.Round(cm / 2.54m, 3));
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
