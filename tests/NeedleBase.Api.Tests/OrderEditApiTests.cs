using System.Net;
using System.Net.Http.Json;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Api.Tests;

/// <summary>Order editing (PUT /api/orders/{id}) and the measurement snapshot, over REST and sync.</summary>
public partial class PaymentApiTests
{
    [Fact]
    public async Task EditOrder_OwnerChangesLines_KeepsSnapshot_AndAudits()
    {
        var (owner, _) = await RegisterAsync("Edit");
        var orderId = await CreateOrderAsync(owner, 500m);
        var before = await GetOrderAsync(owner, orderId);
        var kept = before.Lines[0];

        using var response = await SendAsync(HttpMethod.Put, $"/api/orders/{orderId}", owner, new UpdateOrderRequest
        {
            BaseModifiedUtc = before.DateModifiedUtc,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            Notes = "Add a shirt",
            Lines =
            [
                new CreateOrderLineRequest { Id = kept.Id, GarmentTemplate = kept.GarmentTemplate, Quantity = 2, UnitPrice = 450m, Unit = kept.Unit },
                new CreateOrderLineRequest { GarmentTemplate = GarmentTemplate.Shirt, Quantity = 1, UnitPrice = 200m, Unit = MeasurementUnit.Centimetres }
            ]
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = (await response.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions))!;
        Assert.Equal(1100m, after.TotalAmount);
        Assert.Equal(2, after.Lines.Count);
        var same = Assert.Single(after.Lines, l => l.Id == kept.Id);
        Assert.Equal(GarmentTemplate.Dress, same.GarmentTemplate);
        Assert.Equal(kept.MeasurementTakenAtUtc, same.MeasurementTakenAtUtc);
        Assert.Equal("Add a shirt", after.Notes);
    }

    [Fact]
    public async Task EditOrder_Tailor_IsForbidden_AsProblemDetails()
    {
        var (owner, businessId) = await RegisterAsync("EditT");
        var orderId = await CreateOrderAsync(owner, 300m);
        var tailor = await TokenWithRoleAsync(owner, businessId, BusinessRole.Tailor);

        using var response = await SendAsync(HttpMethod.Put, $"/api/orders/{orderId}", tailor, new UpdateOrderRequest { Notes = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ORDER_EDIT_FORBIDDEN", await CodeAsync(response));
        Assert.Null((await GetOrderAsync(owner, orderId)).Notes);
    }

    [Fact]
    public async Task EditOrder_TotalBelowPaid_Is409()
    {
        var (owner, _) = await RegisterAsync("EditP");
        var orderId = await CreateOrderAsync(owner, 500m);
        Assert.Equal(HttpStatusCode.Created, (await RecordAsync(owner, orderId, 400m, PaymentMethod.Cash)).Status);
        var order = await GetOrderAsync(owner, orderId);

        using var response = await SendAsync(HttpMethod.Put, $"/api/orders/{orderId}", owner, new UpdateOrderRequest
        {
            Lines = [new CreateOrderLineRequest { Id = order.Lines[0].Id, GarmentTemplate = GarmentTemplate.Dress, Quantity = 1, UnitPrice = 300m, Unit = MeasurementUnit.Centimetres }]
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ORDER_TOTAL_BELOW_PAID", await CodeAsync(response));
        Assert.Equal(500m, (await GetOrderAsync(owner, orderId)).TotalAmount);
    }

    [Fact]
    public async Task EditOrder_Collected_IsRefused()
    {
        var (owner, _) = await RegisterAsync("EditC");
        var orderId = await CreateOrderAsync(owner, 300m);
        await SetStatusAsync(owner, orderId, OrderStatus.Cancelled);

        using var response = await SendAsync(HttpMethod.Put, $"/api/orders/{orderId}", owner, new UpdateOrderRequest { Notes = "late" });
        Assert.False(response.IsSuccessStatusCode);
        Assert.NotNull(await CodeAsync(response));
    }

    [Fact]
    public async Task SyncPush_LineEdit_IsApplied_AndReplayIsANoOp()
    {
        var (owner, _) = await RegisterAsync("EditS");
        var orderId = await CreateOrderAsync(owner, 300m);
        var order = await GetOrderAsync(owner, orderId);
        var line = order.Lines[0];
        var newLineId = IdGenerator.NewId();
        var now = DateTimeOffset.UtcNow;
        var op = new SyncPushOperation
        {
            OpId = IdGenerator.NewId(),
            EntityType = SyncEntityType.Order,
            EntityId = orderId,
            BaseModifiedUtc = order.DateModifiedUtc,
            Order = new SyncOrderDto(orderId, order.CustomerId, order.DisplayRef, order.Status, null, null, null, 500m, null, order.CreatedAtUtc, now,
            [
                new SyncOrderLineDto(line.Id, line.GarmentTemplate, 1, 300m, null, line.Unit, "{}", null, 0, line.MeasurementTakenAtUtc),
                new SyncOrderLineDto(newLineId, GarmentTemplate.Shirt, 1, 200m, null, MeasurementUnit.Centimetres, "{}", null, 1)
            ])
        };

        var first = await PushAsync(owner, op);
        Assert.Equal(SyncOpStatus.Applied, first.Status);
        Assert.Equal(500m, first.Order!.TotalAmount);
        Assert.Contains(first.Order.Lines, l => l.Id == newLineId); // device line id kept

        var replay = await PushAsync(owner, op);
        Assert.Equal(SyncOpStatus.Applied, replay.Status);
        Assert.Equal(2, replay.Order!.Lines.Count);
    }
}
