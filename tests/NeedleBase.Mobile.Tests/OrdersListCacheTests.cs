using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Configuration;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

/// <summary>Orders tab speed fixes: cached list + change counter, business id cache, picker null.</summary>
public partial class SyncEdgeCaseTests
{
    private sealed class CountingOrders(IOrderLocalService inner) : IOrderLocalService
    {
        public int Searches;
        public Task<IReadOnlyList<OrderListItemDto>> SearchAsync(string businessId, string? search, string? status, int take = 50, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Searches);
            return inner.SearchAsync(businessId, search, status, take, cancellationToken);
        }
        public Task<OrderDetailDto?> GetByIdAsync(string businessId, string id, CancellationToken cancellationToken = default) => inner.GetByIdAsync(businessId, id, cancellationToken);
        public Task<OrderCountsDto> GetCountsAsync(string businessId, CancellationToken cancellationToken = default) => inner.GetCountsAsync(businessId, cancellationToken);
        public Task<IReadOnlyList<OrderListItemDto>> ListAttentionAsync(string businessId, int take = 5, CancellationToken cancellationToken = default) => inner.ListAttentionAsync(businessId, take, cancellationToken);
        public Task<RecordResult<OrderDetailDto>> SaveAsync(string businessId, CreateOrderRequest request, CancellationToken cancellationToken = default) => inner.SaveAsync(businessId, request, cancellationToken);
        public Task<RecordResult<OrderDetailDto>> UpdateStatusAsync(string businessId, string id, string status, CancellationToken cancellationToken = default) => inner.UpdateStatusAsync(businessId, id, status, cancellationToken);
    }

    private static CreateOrderRequest OneShirt(string customerId) => new()
    {
        CustomerId = customerId,
        Lines =
        [
            new CreateOrderLineRequest
            {
                GarmentTemplate = GarmentTemplate.Shirt, Quantity = 1, UnitPrice = 10m,
                Unit = MeasurementUnit.Centimetres, MeasurementFields = new Dictionary<string, decimal?>()
            }
        ]
    };

    [Fact]
    public async Task OrdersTab_ReusesRows_UntilAnyLocalWrite_StatusChange_OrSessionClear()
    {
        await using var device = await DeviceAsync();
        var customer = (await device.GetRequiredService<ICustomerLocalService>()
            .SaveAsync(Biz, new CreateCustomerRequest("Esi", "024 555 0111", null, null))).Value!;
        var inner = device.GetRequiredService<IOrderLocalService>();
        var first = (await inner.SaveAsync(Biz, OneShirt(customer.Id))).Value!;
        var orders = new CountingOrders(inner);
        var vm = new OrdersViewModel(orders, new FakeAuth());

        await vm.AppearAsync();
        Assert.Equal(1, orders.Searches);
        Assert.Single(vm.Items);

        await vm.AppearAsync();                      // nothing changed: no query
        Assert.Equal(1, orders.Searches);

        await inner.SaveAsync(Biz, OneShirt(customer.Id));   // local save (like sync apply) bumps the counter
        await vm.AppearAsync();
        Assert.Equal(2, orders.Searches);
        Assert.Equal(2, vm.Items.Count);

        await inner.UpdateStatusAsync(Biz, first.Id, OrderStatus.Cutting);
        await vm.AppearAsync();
        Assert.Equal(3, orders.Searches);
        Assert.Contains(vm.Items, r => r.Id == first.Id && r.Item.Status == OrderStatus.Cutting);

        await using (var scope = device.CreateAsyncScope())   // bulk delete (sync tombstones use ExecuteDelete)
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            await db.OrderLines.Where(l => l.OrderId == first.Id).ExecuteDeleteAsync();
            await db.Orders.Where(o => o.Id == first.Id).ExecuteDeleteAsync();
        }

        await vm.AppearAsync();
        Assert.Equal(4, orders.Searches);
        Assert.DoesNotContain(vm.Items, r => r.Id == first.Id);

        vm.ClearSessionState();                      // sign-out / account switch
        Assert.Empty(vm.Items);
        await vm.AppearAsync();
        Assert.Equal(5, orders.Searches);
    }

    [Fact]
    public async Task LocalDataVersion_BumpsOnWipe()
    {
        await using var device = await DeviceAsync();
        var before = LocalDataVersion.Current;
        var service = new LocalDatabaseService(device.GetRequiredService<IServiceScopeFactory>(), new LocalDbOptions { DbPath = "unused" });
        Assert.True(await service.WipeAsync());
        Assert.True(LocalDataVersion.Current > before);
    }

    [Fact]
    public void OrdersTab_PickerNull_FallsBackToAll_WithoutReload()
    {
        var orders = new CountingOrders(new NullOrders());
        var vm = new OrdersViewModel(orders, new FakeAuth());
        vm.SelectedStatusFilter = null;
        Assert.Same(vm.StatusFilters[0], vm.SelectedStatusFilter);
        Assert.Equal(0, orders.Searches);
    }

    [Fact]
    public async Task AuthService_CachesBusinessId_AndClearsItOnSignOut()
    {
        var storage = new MemoryStorage();
        await storage.SetAsync(AuthService.BusinessIdKey, "B1");
        var auth = new AuthService(new StubFactory(new StubHandler(HttpStatusCode.OK, "{}")), storage, new CountingDatabase(), Options.Create(new ApiSettings()));

        Assert.Equal("B1", await auth.GetBusinessIdAsync());
        await storage.SetAsync(AuthService.BusinessIdKey, "B-other");
        Assert.Equal("B1", await auth.GetBusinessIdAsync());   // served from memory

        await auth.ClearTokensAsync();
        Assert.Null(await auth.GetBusinessIdAsync());          // never leaks after sign-out

        await auth.StoreSessionAsync(new NeedleBase.Application.Contracts.Auth.AuthUserDto("U2", "e@x.test", "A", "B", "B2", "Shop", "Owner"));
        Assert.Equal("B2", await auth.GetBusinessIdAsync());    // new account, new id
    }

    private sealed class NullOrders : IOrderLocalService
    {
        public Task<IReadOnlyList<OrderListItemDto>> SearchAsync(string businessId, string? search, string? status, int take = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderListItemDto>>([]);
        public Task<OrderDetailDto?> GetByIdAsync(string businessId, string id, CancellationToken cancellationToken = default) => Task.FromResult<OrderDetailDto?>(null);
        public Task<OrderCountsDto> GetCountsAsync(string businessId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrderListItemDto>> ListAttentionAsync(string businessId, int take = 5, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RecordResult<OrderDetailDto>> SaveAsync(string businessId, CreateOrderRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RecordResult<OrderDetailDto>> UpdateStatusAsync(string businessId, string id, string status, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
