using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace NeedleBase.Mobile.Tests;

/// <summary>Home "Active orders" / "Ready to collect" cards open Orders with a matching filter.</summary>
public partial class SyncEdgeCaseTests
{
    [Fact]
    public void Active_is_every_non_terminal_status()
    {
        Assert.Equal(OrderStatus.All.Where(s => !OrderStatus.IsTerminal(s)), OrderStatus.Active);
        Assert.False(OrderStatus.IsValid(OrderStatus.ActiveFilter)); // a filter key, never a stored status
    }

    [Fact]
    public async Task ActiveFilter_matches_Home_count_offline_and_combines_with_search()
    {
        await using var device = await DeviceAsync();
        var customers = device.GetRequiredService<ICustomerLocalService>();
        var esi = (await customers.SaveAsync(Biz, new CreateCustomerRequest("Esi", "024 555 0111", null, null))).Value!;
        var kojo = (await customers.SaveAsync(Biz, new CreateCustomerRequest("Kojo", "024 555 0222", null, null))).Value!;
        var orders = device.GetRequiredService<IOrderLocalService>();
        var esiNew = (await orders.SaveAsync(Biz, OneShirt(esi.Id))).Value!;
        var esiReady = (await orders.SaveAsync(Biz, OneShirt(esi.Id))).Value!;
        var esiDone = (await orders.SaveAsync(Biz, OneShirt(esi.Id))).Value!;
        var kojoCancelled = (await orders.SaveAsync(Biz, OneShirt(kojo.Id))).Value!;
        var kojoSewing = (await orders.SaveAsync(Biz, OneShirt(kojo.Id))).Value!;
        await orders.UpdateStatusAsync(Biz, esiReady.Id, OrderStatus.Ready);
        await orders.UpdateStatusAsync(Biz, esiDone.Id, OrderStatus.Collected);
        await orders.UpdateStatusAsync(Biz, kojoCancelled.Id, OrderStatus.Cancelled);
        await orders.UpdateStatusAsync(Biz, kojoSewing.Id, OrderStatus.Sewing);

        var counts = await orders.GetCountsAsync(Biz);
        var active = await orders.SearchAsync(Biz, null, OrderStatus.ActiveFilter);
        Assert.Equal(counts.ActiveCount, active.Count);
        Assert.Equal(new[] { esiNew.Id, esiReady.Id, kojoSewing.Id }.Order(), active.Select(o => o.Id).Order());

        var esiActive = await orders.SearchAsync(Biz, "Esi", OrderStatus.ActiveFilter);
        Assert.Equal(new[] { esiNew.Id, esiReady.Id }.Order(), esiActive.Select(o => o.Id).Order());

        var ready = await orders.SearchAsync(Biz, null, OrderStatus.Ready);
        Assert.Equal(counts.ReadyToCollectCount, ready.Count);
    }

    [Fact]
    public async Task DeepLink_preselects_filter_loads_once_and_repeat_taps_reuse_rows()
    {
        await using var device = await DeviceAsync();
        var orders = new CountingOrders(device.GetRequiredService<IOrderLocalService>());
        var vm = new OrdersViewModel(orders, new FakeAuth());

        vm.ApplyQueryAttributes(new Dictionary<string, object> { [OrdersViewModel.FilterQueryKey] = "Active" });
        Assert.Equal(OrderStatus.ActiveFilter, vm.SelectedStatusFilter?.Status);
        Assert.Equal(0, orders.Searches);              // no load from the filter handler; the tab's appear loads
        await vm.AppearAsync();
        Assert.Equal(1, orders.Searches);
        Assert.Equal("No active orders. Everything is collected or cancelled.", vm.EmptyMessage);
        Assert.False(vm.ShowEmptyAdd);

        vm.ApplyQueryAttributes(new Dictionary<string, object> { [OrdersViewModel.FilterQueryKey] = "Active" });
        await vm.AppearAsync();
        Assert.Equal(1, orders.Searches);              // same link again: cached rows

        vm.ApplyQueryAttributes(new Dictionary<string, object>());   // plain tab tap: keeps the visible filter
        await vm.AppearAsync();
        Assert.Equal(OrderStatus.ActiveFilter, vm.SelectedStatusFilter?.Status);
        Assert.Equal(1, orders.Searches);

        vm.ApplyQueryAttributes(new Dictionary<string, object> { [OrdersViewModel.FilterQueryKey] = OrderStatus.Ready });
        await vm.AppearAsync();
        Assert.Equal(OrderStatus.Ready, vm.SelectedStatusFilter?.Status);
        Assert.Equal(2, orders.Searches);
        Assert.Equal("Nothing is ready to collect right now.", vm.EmptyMessage);

        vm.ApplyQueryAttributes(new Dictionary<string, object> { [OrdersViewModel.FilterQueryKey] = "All" });
        await vm.AppearAsync();
        Assert.Null(vm.SelectedStatusFilter?.Status);
        Assert.Equal(3, orders.Searches);

        vm.ApplyFilter("Bogus");                        // unknown key falls back to All
        Assert.Null(vm.SelectedStatusFilter?.Status);
    }

    [Fact]
    public void Home_cards_route_to_matching_filters()
    {
        Assert.Equal("//main/OrdersPage?filter=Active", HomeViewModel.OrdersRoute(OrderStatus.ActiveFilter));
        Assert.Equal("//main/OrdersPage?filter=Ready", HomeViewModel.OrdersRoute(OrderStatus.Ready));
        Assert.Equal("//main/OrdersPage?filter=All", HomeViewModel.OrdersRoute("All"));
        Assert.Equal("No orders match that filter.", OrdersViewModel.EmptyMessageFor("Esi", OrderStatus.ActiveFilter));
    }
}
