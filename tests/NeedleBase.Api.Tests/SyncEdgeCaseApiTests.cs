using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Application.Sync;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;
using NeedleBase.Infrastructure.Sync;

namespace NeedleBase.Api.Tests;

/// <summary>Regression tests for the sync edge-case audit (tombstones, commit order, conflicts, cursors).</summary>
public partial class SyncEndpointTests
{
    [Fact]
    public async Task ReCreate_AfterDelete_RemovesStaleTombstone()
    {
        var token = await RegisterAsync("Recreate");
        var customer = (await PushAsync(token, CustomerUpsert(Customer("Abena"), null))).Customer!;
        var cursor = (await PullAllAsync(token)).Cursor;
        Assert.Equal(SyncOpStatus.Applied, (await PushAsync(token, Delete(SyncEntityType.Customer, customer.Id, customer.DateModifiedUtc))).Status);
        Assert.Equal(SyncOpStatus.Applied, (await PushAsync(token, CustomerUpsert(customer, null))).Status);

        var pulled = await PullAllAsync(token, cursor);
        Assert.Contains(pulled.Customers, c => c.Id == customer.Id);
        Assert.DoesNotContain(pulled.Deleted, d => d.EntityId == customer.Id);
    }

    [Fact]
    public async Task FirstPull_SkipsTombstones()
    {
        var token = await RegisterAsync("FirstPull");
        var customer = (await PushAsync(token, CustomerUpsert(Customer("Kwesi"), null))).Customer!;
        await PushAsync(token, Delete(SyncEntityType.Customer, customer.Id, customer.DateModifiedUtc));

        Assert.Empty((await PullAllAsync(token)).Deleted);
    }

    [Fact]
    public async Task RowFromOpenTransaction_IsPulledAfterCommit_NotSkipped()
    {
        var token = await RegisterAsync("TxOrder");
        var seed = (await PushAsync(token, CustomerUpsert(Customer("Seed"), null))).Customer!;
        string businessId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            businessId = await db.Customers.IgnoreQueryFilters().Where(c => c.Id == seed.Id).Select(c => c.BusinessId).SingleAsync();
        }

        using var holdScope = _factory.Services.CreateScope();
        var hold = holdScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        hold.CurrentBusinessId = businessId;
        var lateId = IdGenerator.NewId();
        string? cursor = null;
        await hold.InTransactionAsync(async ct =>
        {
            hold.Customers.Add(NeedleBase.Domain.Entities.Customer.Create(lateId, businessId, "Late", "+233245550124", null, null, DateTimeOffset.UtcNow));
            await hold.SaveChangesAsync(ct);

            // A row written after the slow transaction started commits first and is pulled now.
            Assert.Equal(SyncOpStatus.Applied, (await PushAsync(token, CustomerUpsert(Customer("Fast"), null))).Status);
            var during = await PullAllAsync(token);
            Assert.DoesNotContain(during.Customers, c => c.Id == lateId);
            cursor = during.Cursor;
            return (true, true);
        }, CancellationToken.None);

        var after = await PullAllAsync(token, cursor);
        Assert.Contains(after.Customers, c => c.Id == lateId);
    }

    [Fact]
    public async Task OrderCreate_WithInvalidStatus_IsRolledBack()
    {
        var token = await RegisterAsync("OrderTx");
        var customer = (await PushAsync(token, CustomerUpsert(Customer("Yaw"), null))).Customer!;
        var orderId = IdGenerator.NewId();
        var now = DateTimeOffset.UtcNow;
        var result = await PushAsync(token, new SyncPushOperation
        {
            OpId = IdGenerator.NewId(),
            EntityType = SyncEntityType.Order,
            EntityId = orderId,
            Order = new SyncOrderDto(orderId, customer.Id, Order0(orderId), "NotAStatus", null, null, null, 150m, null, now, now,
                [new SyncOrderLineDto(IdGenerator.NewId(), GarmentTemplate.Shirt, 1, 150m, null, MeasurementUnit.Centimetres, "{}", null, 0)])
        });

        Assert.Equal(SyncOpStatus.Failed, result.Status);
        Assert.Equal(NeedleBase.Application.Contracts.Errors.OrderErrorMessages.OrderStatusInvalid, result.Code); // created, then the status step failed
        Assert.DoesNotContain((await PullAllAsync(token)).Orders, o => o.Id == orderId);
    }

    [Fact]
    public async Task ForeignIds_LookLikeMissingIds()
    {
        var tokenA = await RegisterAsync("ForA");
        var tokenB = await RegisterAsync("ForB");
        var a = (await PushAsync(tokenA, CustomerUpsert(Customer("Ama"), null))).Customer!;
        var missing = Customer("Nobody");

        var foreignUpdate = await PushAsync(tokenB, CustomerUpsert(a with { Name = "X" }, a.DateModifiedUtc));
        var missingUpdate = await PushAsync(tokenB, CustomerUpsert(missing, missing.DateModifiedUtc));
        Assert.Equal((missingUpdate.Status, missingUpdate.Code), (foreignUpdate.Status, foreignUpdate.Code));

        var foreignDelete = await PushAsync(tokenB, Delete(SyncEntityType.Customer, a.Id, a.DateModifiedUtc));
        var missingDelete = await PushAsync(tokenB, Delete(SyncEntityType.Customer, missing.Id, missing.DateModifiedUtc));
        Assert.Equal((missingDelete.Status, missingDelete.Code), (foreignDelete.Status, foreignDelete.Code));

        var foreignCreate = await PushAsync(tokenB, CustomerUpsert(a, null));
        Assert.Equal((SyncOpStatus.Failed, SyncErrorCodes.Rejected), (foreignCreate.Status, foreignCreate.Code));
    }

    [Fact]
    public async Task DeleteCustomerWithOrders_IsConflict_WithServerCopy()
    {
        var token = await RegisterAsync("Refused");
        var customer = (await PushAsync(token, CustomerUpsert(Customer("Akua"), null))).Customer!;
        var orderId = IdGenerator.NewId();
        var now = DateTimeOffset.UtcNow;
        var orderResult = await PushAsync(token, new SyncPushOperation
        {
            OpId = IdGenerator.NewId(),
            EntityType = SyncEntityType.Order,
            EntityId = orderId,
            Order = new SyncOrderDto(orderId, customer.Id, Order0(orderId), OrderStatus.New, null, null, null, 150m, null, now, now,
                [new SyncOrderLineDto(IdGenerator.NewId(), GarmentTemplate.Shirt, 1, 150m, null, MeasurementUnit.Centimetres, "{}", null, 0)])
        });
        Assert.True(orderResult.Status == SyncOpStatus.Applied, $"{orderResult.Code}: {orderResult.Message}");

        var result = await PushAsync(token, Delete(SyncEntityType.Customer, customer.Id, customer.DateModifiedUtc));
        Assert.Equal(SyncOpStatus.Conflict, result.Status);
        Assert.Equal(SyncErrorCodes.DeleteRefused, result.Code);
        Assert.Equal(customer.Id, result.Customer!.Id);
    }

    [Fact]
    public async Task ChildOfDeletedCustomer_IsParentDeletedConflict()
    {
        var token = await RegisterAsync("Parent");
        var customer = (await PushAsync(token, CustomerUpsert(Customer("Kojo"), null))).Customer!;
        await PushAsync(token, Delete(SyncEntityType.Customer, customer.Id, customer.DateModifiedUtc));
        var now = DateTimeOffset.UtcNow;

        var measurementId = IdGenerator.NewId();
        var measurement = await PushAsync(token, new SyncPushOperation
        {
            OpId = IdGenerator.NewId(),
            EntityType = SyncEntityType.Measurement,
            EntityId = measurementId,
            Measurement = new SyncMeasurementDto(measurementId, customer.Id, GarmentTemplate.Dress, MeasurementUnit.Centimetres, "{}", null, now, now)
        });
        var orderId = IdGenerator.NewId();
        var order = await PushAsync(token, new SyncPushOperation
        {
            OpId = IdGenerator.NewId(),
            EntityType = SyncEntityType.Order,
            EntityId = orderId,
            Order = new SyncOrderDto(orderId, customer.Id, Order0(orderId), OrderStatus.New, null, null, null, 0m, null, now, now, [])
        });

        Assert.Equal((SyncOpStatus.Conflict, SyncErrorCodes.ParentDeleted), (measurement.Status, measurement.Code));
        Assert.Equal((SyncOpStatus.Conflict, SyncErrorCodes.ParentDeleted), (order.Status, order.Code));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExpiredOrOldFormatCursor_Returns400(bool expired)
    {
        var token = await RegisterAsync("Cursor");
        var cursor = expired
            ? new SyncCursor { Cap = 1, IssuedTicks = DateTimeOffset.UtcNow.AddDays(-100).UtcTicks, Customers = new SyncPosition(0, string.Empty) }.Encode()
            : Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"v\":1,\"p\":0}")).TrimEnd('=');
        using var request = Authorized(HttpMethod.Post, "/api/sync/pull", token, new SyncPullRequest { Cursor = cursor, Limit = 50 });
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(SyncErrorCodes.CursorInvalid, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TombstonePurge_RemovesOnlyExpiredRows()
    {
        var token = await RegisterAsync("Purge");
        var seed = (await PushAsync(token, CustomerUpsert(Customer("Seed"), null))).Customer!;
        var oldId = IdGenerator.NewId();
        var newId = IdGenerator.NewId();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var businessId = await db.Customers.IgnoreQueryFilters().Where(c => c.Id == seed.Id).Select(c => c.BusinessId).SingleAsync();
            db.CurrentBusinessId = businessId;
            db.DeletedItems.Add(DeletedItem.Create(oldId, businessId, SyncEntityType.Customer, IdGenerator.NewId(), DateTimeOffset.UtcNow.AddDays(-120)));
            db.DeletedItems.Add(DeletedItem.Create(newId, businessId, SyncEntityType.Customer, IdGenerator.NewId(), DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        Assert.True(await TombstoneCleanupService.PurgeAsync(_factory.Services.GetRequiredService<IServiceScopeFactory>(), DateTimeOffset.UtcNow, CancellationToken.None) >= 1);

        using var check = _factory.Services.CreateScope();
        var ids = await check.ServiceProvider.GetRequiredService<ApplicationDbContext>().DeletedItems.IgnoreQueryFilters()
            .Where(d => d.Id == oldId || d.Id == newId).Select(d => d.Id).ToListAsync();
        Assert.Equal([newId], ids);
    }
}
