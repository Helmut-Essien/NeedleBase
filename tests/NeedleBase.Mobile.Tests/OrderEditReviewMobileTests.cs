using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

/// <summary>Review fixes: query trimming, in-place line merge on pull, edit screen recalculation and locks.</summary>
public partial class OrderEditMobileTests
{
    private sealed class SqlSpy : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            lock (Commands)
            {
                Commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public int Count(string table) { lock (Commands) { return Commands.Count(c => c.Contains($"FROM \"{table}\"", StringComparison.Ordinal)); } }

        public void Clear() { lock (Commands) { Commands.Clear(); } }
    }

    [Fact]
    public async Task Edit_LoadsPaymentsAndProfiles_OnlyWhenNeeded()
    {
        var spy = new SqlSpy();
        await using var device = await DeviceAsync(spy);
        var (order, _) = await SeedAsync(device);
        var orders = device.GetRequiredService<IOrderLocalService>();

        // Header only: neither payments nor profiles are read.
        spy.Clear();
        var header = await orders.UpdateAsync(Biz, order.Id, new UpdateOrderRequest { BaseModifiedUtc = order.DateModifiedUtc, Notes = "Friday" }, BusinessRole.Owner);
        Assert.True(header.IsSuccess, header.Message);
        Assert.Equal(0, spy.Count("Payments"));
        Assert.Equal(0, spy.Count("MeasurementProfiles"));

        // Kept lines only: paid is checked, no profile lookup.
        spy.Clear();
        var kept = await orders.UpdateAsync(Biz, order.Id, Edit(header.Value!, 450m), BusinessRole.Owner);
        Assert.True(kept.IsSuccess, kept.Message);
        Assert.Equal(1, spy.Count("Payments"));
        Assert.Equal(0, spy.Count("MeasurementProfiles"));

        // New garment without measurements: one filtered profile lookup, and it still snapshots the latest dress profile.
        spy.Clear();
        var current = kept.Value!;
        var add = Edit(current, 450m);
        add.Lines!.Add(new CreateOrderLineRequest { GarmentTemplate = GarmentTemplate.Dress, Quantity = 1, UnitPrice = 50m, Unit = MeasurementUnit.Centimetres });
        var added = await orders.UpdateAsync(Biz, order.Id, add, BusinessRole.Owner);
        Assert.True(added.IsSuccess, added.Message);
        Assert.Equal(1, spy.Count("MeasurementProfiles"));
        Assert.Equal(92m, added.Value!.Lines[1].MeasurementFields["bust"]);
    }

    [Fact]
    public async Task Edit_PaidIgnoresVoided_AndOnlyBlocksLoweringItems()
    {
        await using var device = await DeviceAsync();
        var (order, _) = await SeedAsync(device);
        var orders = device.GetRequiredService<IOrderLocalService>();
        var payments = device.GetRequiredService<IPaymentLocalService>();
        var paid = await payments.RecordAsync(Biz, order.Id, new NeedleBase.Application.Contracts.Payments.RecordPaymentRequest { Amount = 400m, Method = PaymentMethod.Cash });
        Assert.True(paid.IsSuccess, paid.Message);
        Assert.True((await payments.VoidAsync(Biz, paid.Value!.Id, "Entered twice by mistake")).IsSuccess);

        var current = (await orders.GetByIdAsync(Biz, order.Id))!;
        var lowered = await orders.UpdateAsync(Biz, order.Id, Edit(current, 300m), BusinessRole.Owner);
        Assert.True(lowered.IsSuccess, lowered.Message); // voided money doesn't count
    }

    [Fact]
    public async Task Pull_MergesLinesInPlace_UpdatingKeptRow()
    {
        var spy = new SqlSpy();
        await using var device = await DeviceAsync(spy);
        var (order, customerId) = await SeedAsync(device);
        await using (var scope = device.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"Customers\" SET \"ServerKnown\" = 1; UPDATE \"MeasurementProfiles\" SET \"ServerKnown\" = 1; UPDATE \"Orders\" SET \"ServerKnown\" = 1; DELETE FROM \"SyncQueue\";");
        }

        var line = order.Lines[0];
        var server = new SyncOrderDto(order.Id, customerId, order.DisplayRef, order.Status, null, null, null, 900m, null,
            order.CreatedAtUtc, order.DateModifiedUtc.AddMinutes(5),
            [new SyncOrderLineDto(line.Id, line.GarmentTemplate, 2, 450m, null, line.Unit, """{"bust":92}""", "longer", 0, line.MeasurementTakenAtUtc)]);
        var api = new OnePull(new SyncPullResponse([new SyncCustomerDto(customerId, "Efua Mensah", "+233245550123", null, null, order.CreatedAtUtc, order.CreatedAtUtc)], [], [server], [], "c", false, DateTimeOffset.UtcNow));
        spy.Commands.Clear();
        await new SyncEngine(device.GetRequiredService<IServiceScopeFactory>(), api).RunAsync(Biz);

        var local = (await device.GetRequiredService<IOrderLocalService>().GetByIdAsync(Biz, order.Id))!;
        var kept = Assert.Single(local.Lines);
        Assert.Equal(line.Id, kept.Id);
        Assert.Equal(2, kept.Quantity);
        Assert.Equal("longer", kept.Notes);
        Assert.Equal(92m, kept.MeasurementFields["bust"]);
        lock (spy.Commands)
        {
            Assert.DoesNotContain(spy.Commands, c => c.Contains("DELETE FROM \"OrderLines\"", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task EditScreen_NotesTyping_DoesNotRecalculate_AndQuantityDoes()
    {
        await using var device = await DeviceAsync();
        var (order, _) = await SeedAsync(device);
        var vm = new EditOrderViewModel(device.GetRequiredService<IOrderLocalService>(), new Auth(BusinessRole.Owner));
        vm.ApplyQueryAttributesForTest(order.Id);
        await vm.LoadAsync();
        var changes = 0;
        vm.PropertyChanged += (_, e) => changes += e.PropertyName == nameof(EditOrderViewModel.TotalText) ? 1 : 0;

        vm.Lines[0].Notes = "hem a little";
        Assert.Equal(0, changes);
        vm.Lines[0].QuantityText = "3";
        Assert.Contains("1,500.00", vm.TotalText);
        Assert.Equal(3, vm.Lines[0].Quantity);
        vm.Lines[0].QuantityText = "x";
        Assert.Null(vm.Lines[0].Quantity);
        Assert.Contains("check quantities", vm.TotalText);
    }

    [Fact]
    public async Task EditScreen_OrderCancelledMeanwhile_ShowsLockedNotStale()
    {
        await using var device = await DeviceAsync();
        var (order, _) = await SeedAsync(device);
        var orders = device.GetRequiredService<IOrderLocalService>();
        var vm = new EditOrderViewModel(orders, new Auth(BusinessRole.Owner));
        vm.ApplyQueryAttributesForTest(order.Id);
        await vm.LoadAsync();
        Assert.True(vm.IsEditable);

        // Arrives while the screen is open (sync pull or another screen).
        Assert.True((await orders.UpdateStatusAsync(Biz, order.Id, OrderStatus.Cancelled)).IsSuccess);
        vm.Lines[0].PriceText = "450";
        await vm.SaveAsync();

        Assert.False(vm.IsEditable);
        Assert.DoesNotContain("changed on this device", vm.ErrorMessage);
        Assert.Equal(500m, (await orders.GetByIdAsync(Biz, order.Id))!.TotalAmount);
    }

    [Fact]
    public async Task EditScreen_FiftyLines_LoadsWithOneTotal_Timed()
    {
        await using var device = await DeviceAsync();
        var (order, _) = await SeedAsync(device);
        var orders = device.GetRequiredService<IOrderLocalService>();
        var big = Edit(order, 10m);
        big.Lines!.AddRange(Enumerable.Range(0, 49).Select(_ => new CreateOrderLineRequest
        {
            GarmentTemplate = GarmentTemplate.Shirt, Quantity = 1, UnitPrice = 10m, Unit = MeasurementUnit.Centimetres
        }));
        Assert.True((await orders.UpdateAsync(Biz, order.Id, big, BusinessRole.Owner)).IsSuccess);

        var vm = new EditOrderViewModel(orders, new Auth(BusinessRole.Owner));
        vm.ApplyQueryAttributesForTest(order.Id);
        var totals = 0;
        vm.PropertyChanged += (_, e) => totals += e.PropertyName == nameof(EditOrderViewModel.TotalText) ? 1 : 0;
        var sw = Stopwatch.StartNew();
        await vm.LoadAsync();
        sw.Stop();
        Assert.Equal(50, vm.Lines.Count);
        Assert.True(totals <= 1, $"total recalculated {totals} times");
        Assert.Contains("500.00", vm.TotalText);

        var type = Stopwatch.StartNew();
        for (var i = 0; i < 200; i++)
        {
            vm.Lines[i % 50].PriceText = (10 + (i % 7)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        type.Stop();
        TimingLog.Write($"mobile-edit-load-50 {sw.Elapsed.TotalMilliseconds:F2}ms; 200 price keystrokes {type.Elapsed.TotalMilliseconds:F2}ms");
    }
}

public class OrderDetailSameLinesTests
{
    private static OrderLineDto L(string id, int qty = 1, string? notes = null) =>
        new(id, GarmentTemplate.Dress, qty, 100m, 100m * qty, null, MeasurementUnit.Centimetres, new Dictionary<string, decimal?> { ["bust"] = 92m }, notes, 0);

    [Fact]
    public void SameLines_KeepsCards_OnlyWhenNothingShownChanged()
    {
        IList<OrderLineDto> shown = [L("a"), L("b")];
        Assert.True(NeedleBase.Mobile.ViewModels.OrderDetailViewModel.SameLines(shown, [L("a"), L("b")]));
        Assert.False(NeedleBase.Mobile.ViewModels.OrderDetailViewModel.SameLines(shown, [L("a"), L("b", 2)]));
        Assert.False(NeedleBase.Mobile.ViewModels.OrderDetailViewModel.SameLines(shown, [L("a"), L("b", notes: "x")]));
        Assert.False(NeedleBase.Mobile.ViewModels.OrderDetailViewModel.SameLines(shown, [L("b"), L("a")]));
        Assert.False(NeedleBase.Mobile.ViewModels.OrderDetailViewModel.SameLines(shown, [L("a")]));
    }
}

internal static class TimingLog
{
    public static void Write(string line)
    {
        if (Environment.GetEnvironmentVariable("NEEDLEBASE_TIMINGS") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
