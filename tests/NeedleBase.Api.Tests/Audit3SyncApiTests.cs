using System.Net;
using System.Net.Http.Json;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Application.Contracts.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Sync;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Tests;

/// <summary>Third-audit fixes on sync: push is lenient for input limits; REST stays strict; phone replay.</summary>
public partial class SyncEndpointTests
{
    [Fact]
    public async Task Push_IsLenient_ForLimitsAddedAfterDataWasQueued_RestStaysStrict()
    {
        var token = await RegisterAsync("Lenient");
        var customer = Customer("Legacy Data") with { Phone = "ask at shop" };          // no valid number on file
        Assert.Equal(SyncOpStatus.Applied, (await PushAsync(token, CustomerUpsert(customer, null))).Status);

        var now = DateTimeOffset.UtcNow;
        var measurement = new SyncMeasurementDto(IdGenerator.NewId(), customer.Id, GarmentTemplate.Shirt, MeasurementUnit.Centimetres,
            """{"chest":400}""", null, now, now);
        var pushedMeasurement = await PushAsync(token, new SyncPushOperation
        {
            OpId = IdGenerator.NewId(), EntityType = SyncEntityType.Measurement, EntityId = measurement.Id, Measurement = measurement
        });
        Assert.Equal(SyncOpStatus.Applied, pushedMeasurement.Status);

        var lines = Enumerable.Range(0, AmountRules.OrderLinesMax + 2)
            .Select(i => new SyncOrderLineDto(IdGenerator.NewId(), GarmentTemplate.Shirt, 1, 2_000_000m, null, MeasurementUnit.Centimetres, "{}", null, i))
            .ToList();
        var order = new SyncOrderDto(IdGenerator.NewId(), customer.Id, "", OrderStatus.New, null, null, null,
            lines.Sum(l => l.UnitPrice), null, now, now, lines);
        var pushedOrder = await PushAsync(token, new SyncPushOperation
        {
            OpId = IdGenerator.NewId(), EntityType = SyncEntityType.Order, EntityId = order.Id, Order = order
        });
        Assert.Equal(SyncOpStatus.Applied, pushedOrder.Status);

        // REST: the unchanged over-limit value keeps notes editable; changing it is checked.
        var update = new UpdateMeasurementRequest
        {
            GarmentTemplate = GarmentTemplate.Shirt, Unit = MeasurementUnit.Centimetres,
            Fields = new Dictionary<string, decimal?> { ["chest"] = 400m }, Notes = "edited on web"
        };
        using (var keep = Authorized(HttpMethod.Put, $"/api/measurements/{measurement.Id}", token, update))
        using (var response = await _client.SendAsync(keep))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        update.Fields["chest"] = 401m;
        using var change = Authorized(HttpMethod.Put, $"/api/measurements/{measurement.Id}", token, update);
        using var changeResponse = await _client.SendAsync(change);
        Assert.Equal(HttpStatusCode.BadRequest, changeResponse.StatusCode);
    }

    [Fact]
    public async Task Push_CustomerReplay_WithLocalPhoneFormat_IsSameCustomer_NotConflict()
    {
        var token = await RegisterAsync("PhoneReplay");
        var customer = Customer("Replay Phone") with { Phone = "024 555 0123" };
        var first = await PushAsync(token, CustomerUpsert(customer, null));
        Assert.Equal(SyncOpStatus.Applied, first.Status);
        Assert.Equal("+233245550123", first.Customer!.Phone);

        // Lost ack: the device sends the same op again with its stale base.
        var again = await PushAsync(token, CustomerUpsert(customer, customer.DateModifiedUtc));
        Assert.Equal(SyncOpStatus.Applied, again.Status);
    }

    [Fact]
    public async Task MeasurementUpdate_StaleBase_Is409()
    {
        var token = await RegisterAsync("StaleMeasure");
        var customer = Customer("Stale M");
        await PushAsync(token, CustomerUpsert(customer, null));
        using var create = Authorized(HttpMethod.Post, $"/api/customers/{customer.Id}/measurements", token, new CreateMeasurementRequest
        {
            GarmentTemplate = GarmentTemplate.Shirt, Unit = MeasurementUnit.Centimetres, Fields = new Dictionary<string, decimal?> { ["chest"] = 90m }
        });
        using var created = await _client.SendAsync(create);
        var m = (await created.Content.ReadFromJsonAsync<MeasurementDetailDto>(JsonOptions))!;

        using var stale = Authorized(HttpMethod.Put, $"/api/measurements/{m.Id}", token, new UpdateMeasurementRequest
        {
            GarmentTemplate = GarmentTemplate.Shirt, Unit = MeasurementUnit.Centimetres, Fields = new Dictionary<string, decimal?> { ["chest"] = 91m },
            BaseModifiedUtc = m.DateModifiedUtc.AddMinutes(-1)
        });
        using var response = await _client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Backfill_OldRows_GetKeysAndPlus233Phone_DateModifiedKept_DevicesPullIt()
    {
        var token = await RegisterAsync("Backfill");
        var customer = Customer("Ɔkyeame Bóateng");
        var pushed = (await PushAsync(token, CustomerUpsert(customer, null))).Customer!;
        var cursor = (await PullAllAsync(token)).Cursor;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Customers\" SET \"Phone\" = '020 111 2233', \"SearchName\" = NULL, \"PhoneDigits\" = NULL WHERE \"Id\" = {customer.Id}");
        }

        await CustomerKeyBackfillService.RunAsync(_factory.Services.GetRequiredService<IServiceScopeFactory>(), CancellationToken.None);

        var changes = await PullAllAsync(token, cursor);
        var row = Assert.Single(changes.Customers, c => c.Id == customer.Id);
        Assert.Equal("+233201112233", row.Phone);
        Assert.True(SyncTime.SameInstant(pushed.DateModifiedUtc, row.DateModifiedUtc));   // not a user edit

        using var list = Authorized(HttpMethod.Get, "/api/customers?search=okyeame", token);
        using var response = await _client.SendAsync(list);
        Assert.Contains("+233201112233", await response.Content.ReadAsStringAsync());
    }
}
