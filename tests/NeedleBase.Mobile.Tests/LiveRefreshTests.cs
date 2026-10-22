using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Helpers;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

/// <summary>Live refresh of visible lists after a background sync changed rows.</summary>
public partial class SyncEdgeCaseTests
{
    [Fact]
    public async Task SyncPull_RaisesChangedBySync_OnlyWhenRowsChanged()
    {
        var biz = "LIVE-" + Guid.NewGuid().ToString("N");
        var raised = 0;
        void Count(string b) { if (b == biz) Interlocked.Increment(ref raised); }
        LocalDataVersion.ChangedBySync += Count;
        try
        {
            await using var device = await DeviceAsync();
            var t = DateTimeOffset.UtcNow;
            await Engine(device, new Pages(Page(customers: [Customer("LC1", "Abena", t)]))).PullAsync(biz);
            Assert.Equal(1, raised);

            await Engine(device, new Pages(Page(customers: [Customer("LC1", "Abena", t)]))).PullAsync(biz);
            await Engine(device, new Pages(Page())).PullAsync(biz);
            Assert.Equal(1, raised); // same copy again / empty page: nothing changed, no reload
        }
        finally
        {
            LocalDataVersion.ChangedBySync -= Count;
        }
    }

    [Fact]
    public async Task LiveListRefresh_MergesBursts_AndStopsWhenDetached()
    {
        var reloads = 0;
        var live = new LiveListRefresh(() => { Interlocked.Increment(ref reloads); return Task.CompletedTask; },
            TimeSpan.FromMilliseconds(60));
        live.Attach();
        for (var i = 0; i < 10; i++)
        {
            LocalDataVersion.NotifyChangedBySync("burst"); // e.g. one per pulled page, from a background thread
            await Task.Delay(5);
        }

        await Task.Delay(400);
        Assert.InRange(reloads, 1, 2); // merged (another test's pull may add one)

        live.Detach();
        var after = reloads;
        LocalDataVersion.NotifyChangedBySync("burst");
        await Task.Delay(300);
        Assert.Equal(after, reloads);
        Assert.False(live.IsAttached);
    }

    [Fact]
    public async Task LiveListRefresh_DetachBeforeDelay_CancelsPendingReload()
    {
        var reloads = 0;
        var live = new LiveListRefresh(() => { reloads++; return Task.CompletedTask; }, TimeSpan.FromMilliseconds(100));
        live.Attach();
        LocalDataVersion.NotifyChangedBySync("x");
        live.Detach(); // tab switched / signed out before the debounce ended
        await Task.Delay(300);
        Assert.Equal(0, reloads);
    }

    [Fact]
    public async Task CustomersLiveReload_KeepsRowsInPlace_NoSpinner_RespectsSearch()
    {
        await using var device = await DeviceAsync();
        var inner = device.GetRequiredService<ICustomerLocalService>();
        await inner.SaveAsync(Biz, new CreateCustomerRequest("Ama", "024 555 0300", null, null));
        await inner.SaveAsync(Biz, new CreateCustomerRequest("Kojo", "024 555 0301", null, null));
        var vm = new CustomersViewModel(inner, new FakeAuth());
        await vm.AppearAsync();
        var kept = vm.Items.ToList();
        var spinner = false;
        var resets = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.ShowBusyIndicator) && vm.ShowBusyIndicator) spinner = true; };
        vm.Items.CollectionChanged += (_, e) => { if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++; };

        await inner.SaveAsync(Biz, new CreateCustomerRequest("Zainab", "024 555 0302", null, null)); // as if pulled
        await vm.LiveReloadAsync();

        Assert.Equal(3, vm.Items.Count);
        Assert.False(spinner);
        Assert.Equal(0, resets);                                  // no Clear: no flicker or scroll jump
        Assert.All(kept, k => Assert.Contains(k, vm.Items));
        Assert.False(vm.IsBusy);
        Assert.False(vm.HasError);
        vm.Disappear();
    }

    [Fact]
    public async Task OrdersLiveReload_SkippedWhileTyping_AndKeepsRowsOnError()
    {
        await using var device = await DeviceAsync();
        var customer = (await device.GetRequiredService<ICustomerLocalService>()
            .SaveAsync(Biz, new CreateCustomerRequest("Esi", "024 555 0303", null, null))).Value!;
        var inner = device.GetRequiredService<IOrderLocalService>();
        await inner.SaveAsync(Biz, OneShirt(customer.Id));
        var flaky = new FlakyOrders(inner);
        var vm = new OrdersViewModel(flaky, new FakeAuth());
        await vm.AppearAsync();
        Assert.Single(vm.Items);
        var row = vm.Items[0];

        await vm.LiveReloadAsync();                 // nothing changed: the same row object stays
        Assert.Same(row, vm.Items[0]);

        flaky.Fail = true;
        await vm.LiveReloadAsync();                 // a quiet reload that fails keeps rows, no error banner
        Assert.Single(vm.Items);
        Assert.False(vm.HasError);
        flaky.Fail = false;

        var before = flaky.Searches;
        vm.SearchText = "Es";                       // typing: the debounced search will read fresh rows
        await vm.LiveReloadAsync();
        Assert.Equal(before, flaky.Searches);
        await Task.Delay(SearchDebouncer.DefaultDelay + TimeSpan.FromMilliseconds(400));
        // At least the debounced search ran; other tests in this process may bump LocalDataVersion meanwhile.
        Assert.True(flaky.Searches >= before + 1, $"expected a search after typing ({before} → {flaky.Searches})");
        vm.ClearSessionState();
    }

    private sealed class FlakyOrders(IOrderLocalService inner) : IOrderLocalService
    {
        public volatile bool Fail;
        public int Searches;
        public Task<IReadOnlyList<NeedleBase.Application.Contracts.Orders.OrderListItemDto>> SearchAsync(string businessId, string? search, string? status, int take = 50, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Searches);
            return Fail ? throw new InvalidOperationException("disk") : inner.SearchAsync(businessId, search, status, take, cancellationToken);
        }
        public Task<NeedleBase.Application.Contracts.Orders.OrderDetailDto?> GetByIdAsync(string businessId, string id, CancellationToken cancellationToken = default) => inner.GetByIdAsync(businessId, id, cancellationToken);
        public Task<OrderCountsDto> GetCountsAsync(string businessId, CancellationToken cancellationToken = default) => inner.GetCountsAsync(businessId, cancellationToken);
        public Task<IReadOnlyList<NeedleBase.Application.Contracts.Orders.OrderListItemDto>> ListAttentionAsync(string businessId, int take = 5, CancellationToken cancellationToken = default) => inner.ListAttentionAsync(businessId, take, cancellationToken);
        public Task<RecordResult<NeedleBase.Application.Contracts.Orders.OrderDetailDto>> SaveAsync(string businessId, NeedleBase.Application.Contracts.Orders.CreateOrderRequest request, CancellationToken cancellationToken = default) => inner.SaveAsync(businessId, request, cancellationToken);
        public Task<RecordResult<NeedleBase.Application.Contracts.Orders.OrderDetailDto>> UpdateStatusAsync(string businessId, string id, string status, CancellationToken cancellationToken = default) => inner.UpdateStatusAsync(businessId, id, status, cancellationToken);
    }
}

public class CollectionSyncTests
{
    [Fact]
    public void Apply_ReplacesOnlyChangedPositions_AddsAndTrimsTail_NeverResets()
    {
        var target = new ObservableCollection<string> { "a", "b", "c", "d" };
        var actions = new List<string>();
        target.CollectionChanged += (_, e) => actions.Add(e.Action.ToString());
        CollectionSync.Apply(target, ["a", "x", "c"], (t, s) => t == s, s => s);
        Assert.Equal(["a", "x", "c"], target);
        Assert.Equal(["Replace", "Remove"], actions);

        actions.Clear();
        CollectionSync.Apply(target, ["a", "x", "c", "e"], (t, s) => t == s, s => s);
        Assert.Equal(["Add"], actions);
    }
}
