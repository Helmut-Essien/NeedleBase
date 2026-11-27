using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

/// <summary>Code-gaps G15b/f on the phone: spaced phone numbers, and open screens that follow a sync.</summary>
public partial class PaymentSyncTests
{
    private static async Task WaitUntilAsync(Func<bool> done, int timeoutMs = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!done() && DateTime.UtcNow < until)
        {
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Open_receipt_shows_spaced_shop_phone_and_turns_synced_after_a_sync()
    {
        await using var device = await DeviceAsync();
        var orderId = await AddOrderAsync(device, 800m);
        var payments = device.GetRequiredService<IPaymentLocalService>();
        var id = (await payments.RecordAsync(Biz, orderId, Request(300m, PaymentMethod.MoMo))).Value!.Id;

        var vm = new PaymentReceiptViewModel(payments, new RoleAuth(BusinessRole.Owner) { BusinessPhone = "0245550123" });
        vm.ApplyQueryAttributesForTest(id);
        await vm.LoadAsync();
        Assert.Equal("+233 24 555 0123", vm.BusinessPhone);
        Assert.True(vm.Receipt!.Payment.IsPending);

        vm.Appear();
        Assert.True(vm.IsListeningForSync);
        await Engine(device, new PayServer()).RunAsync(Biz); // push ack → ChangedBySync → reload
        await WaitUntilAsync(() => vm.Receipt?.Payment.IsPending == false);
        Assert.False(vm.Receipt!.Payment.IsPending);

        vm.Disappear();
        Assert.False(vm.IsListeningForSync);
    }

    [Fact]
    public async Task Push_results_raise_changed_by_sync()
    {
        await using var device = await DeviceAsync();
        await AddOrderAsync(device, 500m);
        var raised = 0;
        void OnChanged(string biz) { if (biz == Biz) Interlocked.Increment(ref raised); }
        LocalDataVersion.ChangedBySync += OnChanged;
        try
        {
            await Engine(device, new PayServer()).RunAsync(Biz);
        }
        finally
        {
            LocalDataVersion.ChangedBySync -= OnChanged;
        }

        Assert.True(raised > 0);
    }

    [Fact]
    public async Task Open_order_detail_reloads_header_after_sync_and_stops_when_closed()
    {
        await using var device = await DeviceAsync();
        var orderId = await AddOrderAsync(device, 500m);
        var orders = device.GetRequiredService<IOrderLocalService>();
        var vm = new OrderDetailViewModel(orders, new RoleAuth(BusinessRole.Owner));
        vm.ApplyQueryAttributesForTest(orderId);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal("+233 24 555 0123", vm.CustomerPhoneDisplay);
        Assert.Equal("+233245550123", vm.CustomerPhone); // raw number stays for WhatsApp and calls
        Assert.Equal(OrderStatus.New, vm.Status);

        await vm.AppearAsync();
        Assert.True(vm.IsListeningForSync);
        Assert.True((await orders.UpdateStatusAsync(Biz, orderId, OrderStatus.Sewing)).IsSuccess); // as if pulled
        LocalDataVersion.NotifyChangedBySync(Biz);
        await WaitUntilAsync(() => vm.Status == OrderStatus.Sewing);
        Assert.Equal(OrderStatus.Sewing, vm.Status);

        vm.Disappear();
        Assert.False(vm.IsListeningForSync);
    }
}

public partial class StaffMobileTests
{
    [Fact]
    public async Task Pull_to_refresh_reloads_and_stops_the_spinner()
    {
        var api = new FakeStaffApi();
        var vm = new StaffViewModel(api);
        vm.IsRefreshing = true;
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(vm.IsRefreshing);
        Assert.Contains("get", api.Calls);
    }

    [Fact]
    public async Task Done_hides_the_code_and_reloads_the_invites()
    {
        var api = new FakeStaffApi();
        var vm = new StaffViewModel(api);
        vm.NewCode = "ABCD-1234";
        await vm.DoneCommand.ExecuteAsync(null);
        Assert.False(vm.HasNewCode);
        Assert.Equal(["get"], api.Calls);
    }
}
