using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

/// <summary>Code-gaps G14: "Assigned to" on the phone, from a cached team list, saved locally then synced.</summary>
public partial class OrderEditMobileTests
{
    private static readonly StaffNameDto Kofi = new("u-kofi", "Kofi Asante", BusinessRole.Tailor);
    private static readonly StaffNameDto AmaOwner = new("u1", "Ama Owusu", BusinessRole.Owner);

    [Fact]
    public async Task StaffDirectory_caches_per_shop_and_falls_back_offline()
    {
        var storage = new Storage();
        var api = new NamesApi([AmaOwner, Kofi]);
        var directory = new StaffDirectory(api, storage);
        Assert.Empty(await directory.GetCachedAsync(Biz));

        Assert.Equal(2, (await directory.RefreshAsync(Biz)).Count);
        api.Offline = true;
        Assert.Equal(2, (await directory.RefreshAsync(Biz)).Count); // offline: the saved list
        Assert.Empty(await directory.GetCachedAsync("another-shop"));

        storage.Values[StaffDirectory.StorageKey] = "{not json";
        Assert.Empty(await directory.GetCachedAsync(Biz));
    }

    [Fact]
    public async Task EditScreen_owner_assigns_then_unassigns_saved_locally_and_queued()
    {
        await using var device = await DeviceAsync();
        var (order, _) = await SeedAsync(device);
        var orders = device.GetRequiredService<IOrderLocalService>();
        var directory = new StaffDirectory(new NamesApi([AmaOwner, Kofi]), new Storage());

        var vm = new EditOrderViewModel(orders, new Auth(BusinessRole.Owner), directory);
        vm.ApplyQueryAttributesForTest(order.Id);
        await vm.LoadAsync();
        await vm.AssigneeRefresh;

        Assert.True(vm.CanAssign);
        Assert.Equal(["Not assigned", "Ama Owusu (Owner)", "Kofi Asante (Tailor)"], vm.AssigneeOptions.Select(o => o.Label));
        Assert.Null(vm.SelectedAssignee!.UserId);
        vm.SelectedAssignee = vm.AssigneeOptions.Single(o => o.UserId == Kofi.UserId);
        await vm.SaveAsync();

        Assert.Equal(Kofi.UserId, (await orders.GetByIdAsync(Biz, order.Id))!.AssignedToUserId);
        await using (var scope = device.CreateAsyncScope())
        {
            var queue = await scope.ServiceProvider.GetRequiredService<LocalDbContext>().SyncQueue.AsNoTracking().ToListAsync();
            Assert.Contains(queue, q => q.EntityId == order.Id && q.PayloadJson!.Contains(Kofi.UserId));
        }

        var again = new EditOrderViewModel(orders, new Auth(BusinessRole.Manager), directory);
        again.ApplyQueryAttributesForTest(order.Id);
        await again.LoadAsync();
        Assert.Equal(Kofi.UserId, again.SelectedAssignee!.UserId); // the cached list shows it straight away
        again.SelectedAssignee = AssigneeOption.None;
        await again.AssigneeRefresh;
        Assert.Null(again.SelectedAssignee!.UserId); // the refresh keeps the choice made meanwhile
        await again.SaveAsync();
        Assert.Null((await orders.GetByIdAsync(Biz, order.Id))!.AssignedToUserId);
    }

    [Fact]
    public async Task EditScreen_offline_with_no_list_keeps_current_assignee()
    {
        await using var device = await DeviceAsync();
        var (order, _) = await SeedAsync(device);
        var orders = device.GetRequiredService<IOrderLocalService>();
        var online = new EditOrderViewModel(orders, new Auth(BusinessRole.Owner), new StaffDirectory(new NamesApi([Kofi]), new Storage()));
        online.ApplyQueryAttributesForTest(order.Id);
        await online.LoadAsync();
        await online.AssigneeRefresh;
        online.SelectedAssignee = online.AssigneeOptions.Single(o => o.UserId == Kofi.UserId);
        await online.SaveAsync();

        var vm = new EditOrderViewModel(orders, new Auth(BusinessRole.Owner), new StaffDirectory(new NamesApi([]) { Offline = true }, new Storage()));
        vm.ApplyQueryAttributesForTest(order.Id);
        await vm.LoadAsync();
        await vm.AssigneeRefresh;
        Assert.Equal(Kofi.UserId, vm.SelectedAssignee!.UserId);
        Assert.Equal("Current person", vm.SelectedAssignee.Label);
        Assert.Equal(Kofi.UserId, vm.BuildRequest()!.AssignedToUserId); // never silently unassigned
    }

    [Theory]
    [InlineData(BusinessRole.Tailor, false)]
    [InlineData(BusinessRole.Owner, true)]
    public async Task EditScreen_picker_hidden_for_tailor_and_free_mode(string role, bool free)
    {
        await using var device = await DeviceAsync();
        var (order, _) = await SeedAsync(device);
        var vm = new EditOrderViewModel(device.GetRequiredService<IOrderLocalService>(), new Auth(role, free),
            new StaffDirectory(new NamesApi([Kofi]), new Storage()));
        vm.ApplyQueryAttributesForTest(order.Id);
        await vm.LoadAsync();
        Assert.False(vm.CanAssign);
    }

    [Fact]
    public async Task DetailScreen_shows_who_it_is_assigned_to_for_everyone()
    {
        await using var device = await DeviceAsync();
        var (order, _) = await SeedAsync(device);
        var orders = device.GetRequiredService<IOrderLocalService>();
        var storage = new Storage();
        var directory = new StaffDirectory(new NamesApi([AmaOwner, Kofi]), storage);

        var detail = new OrderDetailViewModel(orders, new Auth(BusinessRole.Tailor), staff: directory);
        detail.ApplyQueryAttributesForTest(order.Id);
        await detail.LoadCommand.ExecuteAsync(null);
        Assert.Equal("Not assigned", detail.AssigneeText);

        var edit = new EditOrderViewModel(orders, new Auth(BusinessRole.Owner), directory);
        edit.ApplyQueryAttributesForTest(order.Id);
        await edit.LoadAsync();
        await edit.AssigneeRefresh;
        edit.SelectedAssignee = edit.AssigneeOptions.Single(o => o.UserId == Kofi.UserId);
        await edit.SaveAsync();

        var tailorView = new OrderDetailViewModel(orders, new Auth(BusinessRole.Tailor, userId: Kofi.UserId), staff: directory);
        tailorView.ApplyQueryAttributesForTest(order.Id);
        await tailorView.LoadCommand.ExecuteAsync(null);
        Assert.Equal("Assigned to you", tailorView.AssigneeText);

        var offline = new OrderDetailViewModel(orders, new Auth(BusinessRole.Owner),
            staff: new StaffDirectory(new NamesApi([]) { Offline = true }, storage));
        offline.ApplyQueryAttributesForTest(order.Id);
        await offline.LoadCommand.ExecuteAsync(null);
        await offline.AssigneeRefresh;
        Assert.Equal("Assigned to Kofi Asante", offline.AssigneeText); // from the cached list
    }

    private sealed class NamesApi(IReadOnlyList<StaffNameDto> names) : IStaffNamesApi
    {
        public bool Offline { get; set; }

        public Task<StaffCallResult<IReadOnlyList<StaffNameDto>>> GetNamesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Offline
                ? new StaffCallResult<IReadOnlyList<StaffNameDto>>(null, "You're offline.", true)
                : StaffCallResult<IReadOnlyList<StaffNameDto>>.Ok(names));
    }

    private sealed class Storage : ISecureStorageService
    {
        public Dictionary<string, string> Values { get; } = new();
        public Task SetAsync(string key, string value) { Values[key] = value; return Task.CompletedTask; }
        public Task<string?> GetAsync(string key) => Task.FromResult(Values.TryGetValue(key, out var v) ? v : null);
        public void Remove(string key) => Values.Remove(key);
    }
}
