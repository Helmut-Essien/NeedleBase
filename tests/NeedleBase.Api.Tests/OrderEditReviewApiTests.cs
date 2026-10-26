using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Tests;

/// <summary>Review fixes on the order edit path: audit size cap, header-only edits, timings.</summary>
public partial class PaymentApiTests
{
    [Fact]
    public async Task EditOrder_ManyNewLines_AuditKeepsFirst20Details_PlusCount()
    {
        var (owner, _) = await RegisterAsync("EditCap");
        var orderId = await CreateOrderAsync(owner, 100m);
        var before = await GetOrderAsync(owner, orderId);
        var kept = before.Lines[0];
        var lines = new List<CreateOrderLineRequest>
        {
            new() { Id = kept.Id, GarmentTemplate = kept.GarmentTemplate, Quantity = kept.Quantity, UnitPrice = kept.UnitPrice, Unit = kept.Unit }
        };
        lines.AddRange(Enumerable.Range(0, 25).Select(_ => new CreateOrderLineRequest
        {
            GarmentTemplate = GarmentTemplate.Shirt, Quantity = 1, UnitPrice = 10m, Unit = MeasurementUnit.Centimetres
        }));

        using var response = await SendAsync(HttpMethod.Put, $"/api/orders/{orderId}", owner,
            new UpdateOrderRequest { BaseModifiedUtc = before.DateModifiedUtc, Notes = "big", Lines = lines });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var edited = await db.AuditEvents.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(e => e.OrderId == orderId && e.Action == AuditActions.OrderUpdated);
        using var doc = JsonDocument.Parse(edited.ChangesJson!);
        var fields = doc.RootElement.EnumerateArray().Select(c => c.GetProperty("field").GetString()).ToList();
        Assert.Equal(20, fields.Count(f => f == "item added"));
        var more = doc.RootElement.EnumerateArray().Single(c => c.GetProperty("field").GetString() == "more item changes");
        Assert.Equal("5", more.GetProperty("new").GetString());
    }

    [Fact]
    public async Task EditOrder_HeaderOnly_OnOrderPaidAboveTotal_IsAllowed()
    {
        // Paid-vs-total only matters when the items (and so the total) change.
        var (owner, _) = await RegisterAsync("EditHdr");
        var orderId = await CreateOrderAsync(owner, 300m);
        Assert.Equal(HttpStatusCode.Created, (await RecordAsync(owner, orderId, 300m, PaymentMethod.Cash)).Status);
        var before = await GetOrderAsync(owner, orderId);

        using var response = await SendAsync(HttpMethod.Put, $"/api/orders/{orderId}", owner,
            new UpdateOrderRequest { BaseModifiedUtc = before.DateModifiedUtc, Notes = "collect Friday" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("collect Friday", (await GetOrderAsync(owner, orderId)).Notes);
    }

    [Fact]
    public async Task EditOrder_Timing_AgainstDevPostgres()
    {
        var (owner, _) = await RegisterAsync("EditTime");
        var orderId = await CreateOrderAsync(owner, 500m);
        var sw = new Stopwatch();
        var samples = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            var o = await GetOrderAsync(owner, orderId);
            var l = o.Lines[0];
            sw.Restart();
            using var r = await SendAsync(HttpMethod.Put, $"/api/orders/{orderId}", owner, new UpdateOrderRequest
            {
                BaseModifiedUtc = o.DateModifiedUtc,
                Notes = $"n{i}",
                Lines = [new CreateOrderLineRequest { Id = l.Id, GarmentTemplate = l.GarmentTemplate, Quantity = 1 + (i % 3), UnitPrice = 500m, Unit = l.Unit }]
            });
            sw.Stop();
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            if (i >= 5)
            {
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }
        }

        samples.Sort();
        Timings.Write($"api-edit median={samples[samples.Count / 2]:F2}ms p90={samples[(int)(samples.Count * 0.9)]:F2}ms n={samples.Count}");
    }
}

/// <summary>Opt-in timing log (NEEDLEBASE_TIMINGS=path); silent otherwise.</summary>
internal static class Timings
{
    public static void Write(string line)
    {
        if (Environment.GetEnvironmentVariable("NEEDLEBASE_TIMINGS") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
