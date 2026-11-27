using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Application.Expenses;
using NeedleBase.Application.Reports;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;

namespace NeedleBase.Mobile.Tests;

/// <summary>Expenses plan chunk 6 on the phone: schema v13, offline queue + plan hold, sync, purge, remap, wipe, local report.</summary>
[Collection(LocalDataVersionCollection.Name)] // static plan gate + change signals: never in parallel
public class ExpensesMobileTests
{
    private const string Biz = "biz-exp";
    private const string Other = "biz-exp-other";

    private static readonly SubscriptionStatusDto Workshop = SubscriptionStatusDto.Unlocked;
    private static readonly SubscriptionStatusDto Starter = SubscriptionStatusDto.Unlocked with
    {
        PlanCode = NeedleBase.Application.Licensing.PlanCodes.Starter, PlanDisplayName = "Starter", CanReports = false
    };

    // ------------------------------------------------------------------ offline record + sync

    [Fact]
    public async Task Offline_record_survives_reopen_and_syncs_as_one_row()
    {
        LocalSubscriptionGate.Clear(); // a fresh install: the plan isn't known until the first pull
        try
        {
            var path = NewPath();
            string id;
            await using (var phone = await DeviceAsync(path))
            {
                var result = await Expenses(phone).RecordAsync(Biz, Request(120m, ExpenseCategory.Materials, "Thread and buttons"), "u1");
                Assert.True(result.IsSuccess, result.Message);
                id = result.Value!.Id;
                Assert.Equal(26, id.Length); // client ULID
                var item = Assert.Single(await QueueAsync(phone));
                Assert.Equal((SyncEntityType.Expense, id, "Expense GH₵ 120.00"), (item.EntityType, item.EntityId, item.Label));
            }

            var server = new ExpServer { Subscription = Workshop };
            await using (var reopened = await DeviceAsync(path))
            {
                var pending = Assert.Single((await Expenses(reopened).ListAsync(Biz, new ExpenseListQuery())).Value!.Items);
                Assert.True(pending.IsPending);
                Assert.Equal("Recorded on this device", pending.StatusBadge);

                // Push holds it (plan unknown), the pull says Workshop, then the same run sends it.
                await Engine(reopened, server).RunAsync(Biz);
                Assert.Single(server.Expenses);
                Assert.Empty(await QueueAsync(reopened));
                await Engine(reopened, server).RunAsync(Biz);
                Assert.Single(server.Expenses);

                var synced = Assert.Single((await Expenses(reopened).ListAsync(Biz, new ExpenseListQuery())).Value!.Items);
                Assert.False(synced.IsPending);
                Assert.Null(synced.StatusBadge);
                Assert.Equal(id, synced.Id);
            }

            await using var tablet = await DeviceAsync(NewPath());
            await Engine(tablet, server).RunAsync(Biz);
            var pulled = Assert.Single((await Expenses(tablet).ListAsync(Biz, new ExpenseListQuery())).Value!.Items);
            Assert.Equal((id, 120m, "Thread and buttons"), (pulled.Id, pulled.Expense.Amount, pulled.Expense.Note));
            Assert.Empty(await QueueAsync(tablet)); // pulled rows are not re-queued
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    [Fact]
    public async Task Queue_is_held_while_plan_is_off_and_sent_after_upgrade()
    {
        LocalSubscriptionGate.Clear();
        try
        {
            await using var phone = await DeviceAsync(NewPath());
            var id = (await Expenses(phone).RecordAsync(Biz, Request(50m, ExpenseCategory.Transport, null))).Value!.Id;
            var server = new ExpServer { Subscription = Starter };

            await Engine(phone, server).RunAsync(Biz);
            await Engine(phone, server).RunAsync(Biz);
            Assert.DoesNotContain(SyncEntityType.Expense, server.PushLog);
            var held = Assert.Single(await QueueAsync(phone));
            Assert.Equal(SyncQueueState.Pending, held.State); // untouched, not failed

            // Starter known on this phone: new expenses are refused with the upgrade message.
            var refused = await Expenses(phone).RecordAsync(Biz, Request(10m, ExpenseCategory.Other, null));
            Assert.Equal(RecordError.ExpensesNotAllowed, refused.Error);
            Assert.Equal(ExpenseRules.NotAllowedMessage, refused.Message);

            server.Subscription = Workshop; // upgrade
            await Engine(phone, server).RunAsync(Biz);
            Assert.True(server.Expenses.ContainsKey(id));
            Assert.Empty(await QueueAsync(phone));
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    [Fact]
    public async Task Server_refusal_for_plan_keeps_the_expense_queued()
    {
        LocalSubscriptionGate.Set(Biz, Workshop); // phone still thinks Workshop; the server already moved to Starter
        try
        {
            await using var phone = await DeviceAsync(NewPath());
            Assert.True((await Expenses(phone).RecordAsync(Biz, Request(75m, ExpenseCategory.Equipment, null))).IsSuccess);
            var server = new ExpServer { Subscription = Starter, RefuseExpenses = true };

            await Engine(phone, server).PushAsync(Biz);
            var item = Assert.Single(await QueueAsync(phone));
            Assert.Equal(SyncQueueState.Pending, item.State);
            Assert.Equal(0, item.RetryCount);
            Assert.Equal(ExpenseRules.NotAllowedMessage, item.LastError);
            Assert.Empty(server.Expenses);
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    [Fact]
    public async Task Void_offline_excluded_from_totals_idempotent_and_synced()
    {
        LocalSubscriptionGate.Set(Biz, Workshop);
        try
        {
            await using var phone = await DeviceAsync(NewPath());
            var service = Expenses(phone);
            var orderId = await AddOrderAsync(phone, 400m);
            var keep = (await service.RecordAsync(Biz, Request(30m, ExpenseCategory.Materials, "lining", orderId))).Value!;
            var wrong = (await service.RecordAsync(Biz, Request(200m, ExpenseCategory.StaffPay, "Kofi"))).Value!;
            Assert.Equal("Order #" + keep.OrderDisplayRef + " · Efua Mensah", (await service.GetAsync(Biz, keep.Id))!.OrderLine);
            var server = new ExpServer { Subscription = Workshop };
            await Engine(phone, server).RunAsync(Biz);

            Assert.Equal(RecordError.ExpenseInvalid, (await service.VoidAsync(Biz, wrong.Id, "ab")).Error);
            Assert.True((await service.VoidAsync(Biz, wrong.Id, "wrong amount", "u1")).Value!.IsVoided);
            Assert.Equal("wrong amount", (await service.VoidAsync(Biz, wrong.Id, "again")).Value!.VoidReason); // first reason wins
            Assert.Equal(RecordError.ExpenseNotFound, (await service.VoidAsync(Biz, "missing", "typo")).Error);

            var all = (await service.ListAsync(Biz, new ExpenseListQuery())).Value!;
            Assert.Equal((30m, 1, 2), (all.Total, all.Count, all.Items.Count)); // voided listed, never counted
            Assert.Equal("Voided", all.Items.Single(i => i.Id == wrong.Id).StatusBadge);
            Assert.Single((await service.ListAsync(Biz, new ExpenseListQuery { IncludeVoided = false })).Value!.Items);
            Assert.Equal(keep.Id, Assert.Single((await service.ListAsync(Biz, new ExpenseListQuery { OrderId = orderId })).Value!.Items).Id);
            Assert.Empty((await service.ListAsync(Biz, new ExpenseListQuery { Category = ExpenseCategory.Transport })).Value!.Items);
            Assert.Equal(RecordError.ExpenseFilterInvalid, (await service.ListAsync(Biz, new ExpenseListQuery { Category = "Food" })).Error);
            Assert.Equal(RecordError.ExpenseFilterInvalid, (await service.ListAsync(Biz, new ExpenseListQuery { Month = "2019-12" })).Error);
            var lastMonth = ReportRules.Key(ReportRules.FirstOfMonth(Today()).AddMonths(-1));
            Assert.Empty((await service.ListAsync(Biz, new ExpenseListQuery { Month = lastMonth })).Value!.Items);

            await Engine(phone, server).RunAsync(Biz);
            Assert.True(server.Expenses[wrong.Id].IsVoided);
            Assert.Equal("wrong amount", server.Expenses[wrong.Id].VoidReason);
            Assert.Empty(await QueueAsync(phone));
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    [Fact]
    public async Task Record_rules_match_the_server_and_same_id_replays()
    {
        LocalSubscriptionGate.Set(Biz, Workshop);
        try
        {
            await using var phone = await DeviceAsync(NewPath());
            var service = Expenses(phone);
            Assert.Equal(RecordError.ExpenseInvalid, (await service.RecordAsync(Biz, Request(0m, ExpenseCategory.Other, null))).Error);
            Assert.Equal(RecordError.ExpenseInvalid, (await service.RecordAsync(Biz, Request(1.005m, ExpenseCategory.Other, null))).Error);
            Assert.Equal(RecordError.ExpenseInvalid, (await service.RecordAsync(Biz, Request(5m, "Food", null))).Error);
            var future = Request(5m, ExpenseCategory.Other, null);
            future.SpentOn = Today().AddDays(1);
            Assert.Equal(RecordError.ExpenseInvalid, (await service.RecordAsync(Biz, future)).Error);
            Assert.Equal(RecordError.ExpenseOrderInvalid, (await service.RecordAsync(Biz, Request(5m, ExpenseCategory.Other, null, "missing"))).Error);

            var request = Request(12.5m, ExpenseCategory.Transport, "trotro");
            request.Id = IdGenerator.NewId();
            Assert.True((await service.RecordAsync(Biz, request)).IsSuccess);
            Assert.True((await service.RecordAsync(Biz, request)).IsSuccess); // double tap: same expense
            request.Amount = 13m;
            Assert.Equal(RecordError.ExpenseConflict, (await service.RecordAsync(Biz, request)).Error);
            Assert.Single(await QueueAsync(phone));
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    [Fact]
    public async Task Mismatch_conflict_keep_mine_keeps_both()
    {
        LocalSubscriptionGate.Set(Biz, Workshop);
        try
        {
            await using var phone = await DeviceAsync(NewPath());
            var mine = (await Expenses(phone).RecordAsync(Biz, Request(40m, ExpenseCategory.Materials, null))).Value!;
            var t = DateTimeOffset.UtcNow;
            var server = new ExpServer { Subscription = Workshop };
            server.Expenses[mine.Id] = new SyncExpenseDto(mine.Id, 99m, ExpenseCategory.Other, Today(), null, null, false, null, null, t, t);

            await Engine(phone, server).PushAsync(Biz);
            var conflict = Assert.Single(await QueueAsync(phone));
            Assert.Equal((SyncQueueState.Conflict, SyncErrorCodes.ExpenseMismatch), (conflict.State, conflict.ConflictCode));
            Assert.Equal("A different expense with this id was saved elsewhere. Keep both or keep the saved one.", conflict.LastError);

            Assert.True(await Engine(phone, server).ResolveConflictAsync(Biz, conflict.Id, keepMine: true));
            await Engine(phone, server).RunAsync(Biz);
            var rows = (await Expenses(phone).ListAsync(Biz, new ExpenseListQuery())).Value!;
            Assert.Equal([40m, 99m], rows.Items.Select(i => i.Expense.Amount).Order());
            Assert.Equal(2, server.Expenses.Count);
            Assert.Empty(await QueueAsync(phone));
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    [Fact]
    public async Task Pulled_expense_whose_order_is_not_here_shows_order_removed()
    {
        LocalSubscriptionGate.Clear();
        try
        {
            var t = DateTimeOffset.UtcNow;
            var server = new ExpServer { Subscription = Workshop };
            server.Expenses["E1"] = new SyncExpenseDto("E1", 15m, ExpenseCategory.Materials, Today(), null, "gone-order", false, null, null, t, t);
            server.Expenses["E2"] = new SyncExpenseDto("E2", 5m, ExpenseCategory.Other, Today(), "zip " + Expense.OrderRemovedMarker, null, false, null, null, t, t);
            await using var phone = await DeviceAsync(NewPath());
            await Engine(phone, server).RunAsync(Biz);

            var service = Expenses(phone);
            Assert.Equal("Order removed", (await service.GetAsync(Biz, "E1"))!.OrderLine);
            Assert.Equal("Order removed", (await service.GetAsync(Biz, "E2"))!.OrderLine);
            Assert.Equal(20m, (await service.ListAsync(Biz, new ExpenseListQuery())).Value!.Total);
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    // ------------------------------------------------------------------ purge, migration, remap, wipe

    [Fact]
    public async Task Tailor_purge_drops_synced_copies_but_keeps_queued_ones()
    {
        LocalSubscriptionGate.Set(Biz, Workshop);
        try
        {
            var t = DateTimeOffset.UtcNow;
            var server = new ExpServer { Subscription = Workshop };
            server.Expenses["E1"] = new SyncExpenseDto("E1", 60m, ExpenseCategory.RentAndBills, Today(), null, null, false, null, null, t, t);
            await using var phone = await DeviceAsync(NewPath());
            await Engine(phone, server).RunAsync(Biz);
            var queued = (await Expenses(phone).RecordAsync(Biz, Request(8m, ExpenseCategory.Transport, null))).Value!.Id;

            Assert.Equal(1, await Expenses(phone).PurgeHiddenAsync(Biz));
            var left = Assert.Single((await Expenses(phone).ListAsync(Biz, new ExpenseListQuery())).Value!.Items);
            Assert.Equal(queued, left.Id);
            await using (var scope = phone.CreateAsyncScope())
            {
                Assert.True((await scope.ServiceProvider.GetRequiredService<LocalDbContext>().DeviceInfo.SingleAsync(d => d.BusinessId == Biz)).ResyncPending);
            }

            Assert.Equal(0, await Expenses(phone).PurgeHiddenAsync(Biz)); // nothing left to drop: no second resync
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    [Fact]
    public async Task Version_12_database_migrates_to_13_and_keeps_data()
    {
        var path = NewPath();
        await using (var phone = await DeviceAsync(path))
        {
            await AddOrderAsync(phone, 100m);
            await using var scope = phone.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                DROP TABLE "Expenses";
                PRAGMA user_version = 12;
                """);
        }

        await using var upgraded = await DeviceAsync(path); // runs the migrator
        await using (var scope = upgraded.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            Assert.Equal(13, LocalSchemaMigrator.LatestVersion);
            Assert.Equal(LocalSchemaMigrator.LatestVersion, await LocalSchemaMigrator.MigrateAsync(db));
            Assert.Equal(1, await db.Orders.CountAsync());
            Assert.Equal(0, await db.Expenses.CountAsync());
            var indexes = await db.Database.SqlQueryRaw<string>(
                "SELECT name AS \"Value\" FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Expenses' AND name NOT LIKE 'sqlite_%' ORDER BY name").ToListAsync();
            Assert.Equal(["IX_Expenses_BusinessId_OrderId", "IX_Expenses_BusinessId_SpentOn", "IX_Expenses_Unknown"], indexes);
        }

        LocalSubscriptionGate.Clear();
        var saved = await Expenses(upgraded).RecordAsync(Biz, Request(9.99m, ExpenseCategory.Other, "after upgrade"));
        Assert.True(saved.IsSuccess, saved.Message);
        Assert.Equal(9.99m, (await Expenses(upgraded).GetAsync(Biz, saved.Value!.Id))!.Expense.Amount);
    }

    [Fact]
    public async Task Free_upgrade_remaps_expenses_to_the_online_shop()
    {
        const string from = "01FREEEXPENSEREMAPFROM00001";
        const string to = "01FREEEXPENSEREMAPTO0000002";
        await using var phone = await DeviceAsync(NewPath());
        LocalFreeModeGate.Set(from);
        try
        {
            await using (var scope = phone.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
                var now = DateTimeOffset.UtcNow;
                db.Expenses.Add(Expense.Create(IdGenerator.NewId(), from, 25m, ExpenseCategory.Materials, Today(), null, null, null, Today(), now));
                await db.SaveChangesAsync();
            }

            Assert.Empty(await QueueAsync(phone)); // Free never queues
            var upgrade = new FreeUpgradeLocalService(phone.GetRequiredService<IServiceScopeFactory>());
            Assert.True(await upgrade.HasShopRowsAsync(from));
            await upgrade.RemapBusinessIdAsync(from, to);
            Assert.False(await upgrade.HasShopRowsAsync(from));
        }
        finally
        {
            LocalFreeModeGate.Clear();
        }

        LocalSubscriptionGate.Clear();
        try
        {
            Assert.Equal(25m, (await Expenses(phone).ListAsync(to, new ExpenseListQuery())).Value!.Total);
            Assert.Empty((await Expenses(phone).ListAsync(from, new ExpenseListQuery())).Value!.Items);
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    [Fact]
    public async Task Sign_out_wipe_removes_expenses_and_their_queue()
    {
        LocalSubscriptionGate.Set(Biz, Workshop);
        await using var phone = await DeviceAsync(NewPath());
        Assert.True((await Expenses(phone).RecordAsync(Biz, Request(18m, ExpenseCategory.Transport, null))).IsSuccess);

        Assert.True(await new LocalDatabaseService(phone.GetRequiredService<IServiceScopeFactory>(), new LocalDbOptions { DbPath = "unused" }).WipeAsync());
        Assert.Null(LocalSubscriptionGate.Current);
        Assert.Empty((await Expenses(phone).ListAsync(Biz, new ExpenseListQuery())).Value!.Items);
        Assert.Empty(await QueueAsync(phone));
    }

    // ------------------------------------------------------------------ local report

    [Fact]
    public async Task Local_report_profit_equals_the_server_calculator()
    {
        LocalSubscriptionGate.Set(Biz, Workshop);
        try
        {
            await using var phone = await DeviceAsync(NewPath());
            var today = Today();
            var month = ReportRules.FirstOfMonth(today);
            var orderId = await AddOrderAsync(phone, 300m);
            Assert.True((await phone.GetRequiredService<IPaymentLocalService>()
                .RecordAsync(Biz, orderId, new RecordPaymentRequest { Amount = 100m, Method = PaymentMethod.Cash })).IsSuccess);

            var service = Expenses(phone);
            Assert.True((await service.RecordAsync(Biz, Request(130m, ExpenseCategory.Materials, null, orderId))).IsSuccess);
            Assert.True((await service.RecordAsync(Biz, Request(20m, ExpenseCategory.Transport, null))).IsSuccess);
            var voided = (await service.RecordAsync(Biz, Request(500m, ExpenseCategory.StaffPay, null))).Value!;
            Assert.True((await service.VoidAsync(Biz, voided.Id, "typo")).IsSuccess);
            var lastMonth = Request(40m, ExpenseCategory.RentAndBills, null);
            lastMonth.SpentOn = month.AddMonths(-1);
            Assert.True((await service.RecordAsync(Biz, lastMonth)).IsSuccess);
            Assert.True((await service.RecordAsync(Other, Request(999m, ExpenseCategory.Other, null))).IsSuccess); // another shop

            var r = await phone.GetRequiredService<IReportLocalService>().GetAsync(Biz, month, today);
            Assert.Equal((100m, 150m, -50m, 40m), (r.MoneyReceived, r.Spent, r.Profit, r.PreviousSpent));
            Assert.True(r.IsLoss);
            Assert.Equal([(ExpenseCategory.Materials, 130m), (ExpenseCategory.Transport, 20m)],
                r.SpentByCategory.Select(c => (c.Category, c.Total)));

            // Same rows straight into the server's calculator: identical report.
            Order order;
            await using (var scope = phone.CreateAsyncScope())
            {
                order = await scope.ServiceProvider.GetRequiredService<LocalDbContext>().Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            }

            var paidAt = (await phone.GetRequiredService<IPaymentLocalService>().GetForOrderAsync(Biz, orderId))!.Payments.Single().Payment.RecordedAtUtc;
            var expected = MonthlyReportCalculator.Build(month, today,
                [new ReportOrderRow(order.Status, order.TotalAmount, order.CreatedAtUtc, 100m)],
                [new ReportPaymentRow(100m, PaymentMethod.Cash, paidAt)],
                [
                    new ReportExpenseRow(130m, ExpenseCategory.Materials, today, false),
                    new ReportExpenseRow(20m, ExpenseCategory.Transport, today, false),
                    new ReportExpenseRow(500m, ExpenseCategory.StaffPay, today, true),
                    new ReportExpenseRow(40m, ExpenseCategory.RentAndBills, month.AddMonths(-1), false),
                ]);
            Assert.Equal(Json(expected), Json(r)); // decimal scale aside (SQLite TEXT gives 100.0), every figure matches
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    // ------------------------------------------------------------------ helpers

    private static string Json(object value) =>
        System.Text.RegularExpressions.Regex.Replace(JsonSerializer.Serialize(value), @"(\d)\.0+(?=[,}\]])", "$1");

    private static DateOnly Today() => BusinessTime.Today(DateTimeOffset.UtcNow);

    private static RecordExpenseRequest Request(decimal amount, string category, string? note, string? orderId = null) =>
        new() { Amount = amount, Category = category, Note = note, OrderId = orderId };

    private static IExpenseLocalService Expenses(ServiceProvider device) => device.GetRequiredService<IExpenseLocalService>();

    private static SyncEngine Engine(ServiceProvider device, ISyncApi api) => new(device.GetRequiredService<IServiceScopeFactory>(), api);

    private static string NewPath() => Path.Combine(Path.GetTempPath(), $"needlebase-exp-{Guid.NewGuid():N}.db3");

    private static async Task<List<SyncQueueItem>> QueueAsync(ServiceProvider device)
    {
        await using var scope = device.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LocalDbContext>().SyncQueue.AsNoTracking().ToListAsync();
    }

    private static async Task<string> AddOrderAsync(ServiceProvider device, decimal total)
    {
        var (c, error) = await device.GetRequiredService<ICustomerLocalService>()
            .SaveAsync(Biz, new CreateCustomerRequest("Efua Mensah", "0245550123", null, null));
        Assert.Null(error);
        var (o, orderError) = await device.GetRequiredService<IOrderLocalService>().SaveAsync(Biz, new CreateOrderRequest
        {
            CustomerId = c!.Id,
            TotalAmount = total,
            Lines =
            [
                new CreateOrderLineRequest
                {
                    GarmentTemplate = GarmentTemplate.Dress, Quantity = 1, UnitPrice = total,
                    Unit = MeasurementUnit.Centimetres, MeasurementFields = new Dictionary<string, decimal?>()
                }
            ]
        });
        Assert.Null(orderError);
        return o!.Id;
    }

    private static async Task<ServiceProvider> DeviceAsync(string path)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LocalDbContext>(o => o.UseSqlite($"Data Source={path};Pooling=False"));
        services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
        services.AddSingleton<IOrderLocalService, OrderLocalService>();
        services.AddSingleton<IPaymentLocalService, PaymentLocalService>();
        services.AddSingleton<IExpenseLocalService, ExpenseLocalService>();
        services.AddSingleton<IReportLocalService, ReportLocalService>();
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await LocalSchemaMigrator.MigrateAsync(scope.ServiceProvider.GetRequiredService<LocalDbContext>());
        return provider;
    }

    /// <summary>In-memory server with the API's expense rules (immutable, one-way void, mismatch = conflict, plan gate).</summary>
    private sealed class ExpServer : ISyncApi
    {
        private long _tick;
        private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;

        public SubscriptionStatusDto? Subscription { get; set; }
        public bool RefuseExpenses { get; set; }
        public Dictionary<string, SyncCustomerDto> Customers { get; } = new();
        public Dictionary<string, SyncOrderDto> Orders { get; } = new();
        public Dictionary<string, SyncPaymentDto> Payments { get; } = new();
        public Dictionary<string, SyncExpenseDto> Expenses { get; } = new();
        public List<string> PushLog { get; } = new();

        private DateTimeOffset Now() => _start.AddMilliseconds(++_tick);

        public Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken)
        {
            var results = new List<SyncPushResult>();
            foreach (var op in request.Operations)
            {
                PushLog.Add(op.EntityType);
                results.Add(Apply(op));
            }

            return Task.FromResult(new SyncPushResponse(results));
        }

        private SyncPushResult Apply(SyncPushOperation op)
        {
            switch (op.EntityType)
            {
                case SyncEntityType.Customer:
                {
                    var saved = op.Customer! with { DateModifiedUtc = Now() };
                    Customers[op.EntityId] = saved;
                    return new(op.OpId, SyncOpStatus.Applied, null, null, saved);
                }

                case SyncEntityType.Order:
                {
                    var saved = op.Order! with { DateModifiedUtc = Now() };
                    Orders[op.EntityId] = saved;
                    return new(op.OpId, SyncOpStatus.Applied, null, null, Order: saved);
                }

                case SyncEntityType.Payment:
                {
                    var saved = op.Payment! with { DateModifiedUtc = Now() };
                    Payments[op.EntityId] = saved;
                    return new(op.OpId, SyncOpStatus.Applied, null, null, Payment: saved);
                }

                default:
                {
                    if (RefuseExpenses)
                        return new(op.OpId, SyncOpStatus.Failed, SyncErrorCodes.ExpensesNotAllowed, ExpenseRules.NotAllowedMessage);
                    var dto = op.Expense!;
                    if (Expenses.TryGetValue(op.EntityId, out var existing))
                    {
                        if (existing.Amount != dto.Amount || existing.Category != dto.Category || existing.SpentOn != dto.SpentOn)
                            return new(op.OpId, SyncOpStatus.Conflict, SyncErrorCodes.ExpenseMismatch, "x", Expense: existing);
                        if (dto.IsVoided && !existing.IsVoided)
                            Expenses[op.EntityId] = existing = existing with
                            {
                                IsVoided = true, VoidedAtUtc = dto.VoidedAtUtc, VoidReason = dto.VoidReason, DateModifiedUtc = Now()
                            };
                        return new(op.OpId, SyncOpStatus.Applied, null, null, Expense: existing);
                    }

                    var saved = dto with { DateModifiedUtc = Now() };
                    Expenses[op.EntityId] = saved;
                    return new(op.OpId, SyncOpStatus.Applied, null, null, Expense: saved);
                }
            }
        }

        public Task<SyncPullResponse> PullAsync(SyncPullRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new SyncPullResponse(Customers.Values.ToList(), [], Orders.Values.ToList(), [], "c", false, DateTimeOffset.UtcNow)
            {
                Payments = Payments.Values.ToList(),
                Expenses = Subscription?.CanReports == true ? Expenses.Values.ToList() : null, // phase skipped off-plan
                Subscription = Subscription
            });
    }
}
