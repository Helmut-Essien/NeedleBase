using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Application.Expenses;
using NeedleBase.Application.Reports;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

/// <summary>Expenses plan chunk 7: phone screens (list, record, detail, reports, sync details) through their view models.</summary>
[Collection(LocalDataVersionCollection.Name)] // static plan gate + change signals: never in parallel
public class ExpensesUiTests
{
    private const string Biz = "biz-exp-ui";

    private static readonly SubscriptionStatusDto Workshop = SubscriptionStatusDto.Unlocked;
    private static readonly SubscriptionStatusDto Starter = SubscriptionStatusDto.Unlocked with
    {
        PlanCode = NeedleBase.Application.Licensing.PlanCodes.Starter, PlanDisplayName = "Starter", CanReports = false
    };

    // ------------------------------------------------------------------ gate

    [Fact]
    public void Gate_closes_for_free_tailor_starter_and_unknown_plan_and_opens_for_workshop_owner_and_manager()
    {
        Assert.Equal(ExpenseAccess.FreeMessage, ExpenseAccess.Gate(true, BusinessRole.Owner, Workshop));
        Assert.Equal(ExpenseRules.ForbiddenMessage, ExpenseAccess.Gate(false, BusinessRole.Tailor, Workshop));
        Assert.Equal(ExpenseRules.NotAllowedMessage, ExpenseAccess.Gate(false, BusinessRole.Owner, Starter));
        Assert.Equal(ExpenseRules.NotAllowedMessage, ExpenseAccess.Gate(false, BusinessRole.Owner, null));
        Assert.Null(ExpenseAccess.Gate(false, BusinessRole.Owner, Workshop));
        Assert.Null(ExpenseAccess.Gate(false, BusinessRole.Manager, Workshop));
    }

    [Fact]
    public async Task List_page_for_a_tailor_or_starter_shows_why_and_no_record_button()
    {
        try
        {
            await using var phone = await DeviceAsync(NewPath());
            LocalSubscriptionGate.Set(Biz, Workshop);
            var tailor = ListVm(phone, BusinessRole.Tailor);
            await tailor.LoadAsync();
            Assert.Equal(ExpenseRules.ForbiddenMessage, tailor.Message);
            Assert.False(tailor.CanRecord);
            Assert.False(tailor.ShowContent);

            LocalSubscriptionGate.Set(Biz, Starter);
            var owner = ListVm(phone, BusinessRole.Owner);
            await owner.LoadAsync();
            Assert.Equal(ExpenseRules.NotAllowedMessage, owner.Message);
            Assert.False(owner.CanRecord);

            var free = ListVm(phone, BusinessRole.Owner, free: true);
            await free.LoadAsync();
            Assert.Equal(ExpenseAccess.FreeMessage, free.Message);
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    // ------------------------------------------------------------------ list

    [Fact]
    public async Task List_shows_month_rows_summary_filter_and_empty_texts()
    {
        try
        {
            await using var phone = await DeviceAsync(NewPath());
            LocalSubscriptionGate.Set(Biz, Workshop);
            var today = BusinessTime.Today(DateTimeOffset.UtcNow);
            await Expenses(phone).RecordAsync(Biz, Request(120m, ExpenseCategory.Materials, "Lining\nand zips", today), "u1");
            await Expenses(phone).RecordAsync(Biz, Request(80m, ExpenseCategory.RentAndBills, null, today), "u1");

            var vm = ListVm(phone, BusinessRole.Owner);
            await vm.LoadAsync();
            Assert.Null(vm.Message);
            Assert.True(vm.CanRecord);
            Assert.Equal(2, vm.Items.Count);
            Assert.Equal(7, vm.Chips.Count); // All + 6 categories
            Assert.Equal($"Spent in {vm.MonthLabel}: GH₵ 200.00 · 2 expenses", vm.SummaryText);
            var lining = vm.Items.Single(i => i.Category == ExpenseCategory.Materials);
            Assert.Equal("Lining", lining.NoteLine);
            Assert.Equal(today.ToString("dd MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), lining.DateDisplay);
            Assert.True(lining.ShowPending);
            Assert.Equal("Not synced yet", ExpenseRow.PendingText);
            Assert.False(vm.CanGoNext); // no future months

            await vm.SelectCategoryCommand.ExecuteAsync(ExpenseCategory.StaffPay);
            Assert.Empty(vm.Items);
            Assert.True(vm.ShowEmpty);
            Assert.Contains("Choose All to see every expense.", vm.EmptyText);

            await vm.SelectCategoryCommand.ExecuteAsync(null);
            Assert.Equal(2, vm.Items.Count);

            await vm.PreviousCommand.ExecuteAsync(null);
            Assert.Empty(vm.Items);
            Assert.StartsWith("No expenses in ", vm.EmptyText);
            Assert.True(vm.CanGoNext);
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    [Fact]
    public async Task List_month_query_is_clamped_to_jan_2020_and_this_month()
    {
        await using var phone = await DeviceAsync(NewPath());
        var vm = ListVm(phone, BusinessRole.Owner);
        vm.ApplyQueryAttributes(new Dictionary<string, object> { ["month"] = "2020-01" });
        Assert.Equal(ReportRules.EarliestMonth, vm.Month);
        Assert.False(vm.CanGoPrevious);
        await vm.PreviousCommand.ExecuteAsync(null);
        Assert.Equal(ReportRules.EarliestMonth, vm.Month);
    }

    [Fact]
    public async Task Read_only_shop_lists_but_cannot_record()
    {
        try
        {
            await using var phone = await DeviceAsync(NewPath());
            LocalSubscriptionGate.Set(Biz, Workshop with { IsReadOnly = true });
            var vm = ListVm(phone, BusinessRole.Owner);
            await vm.LoadAsync();
            Assert.Null(vm.Message);
            Assert.True(vm.IsReadOnly);
            Assert.False(vm.CanRecord);
            string? went = null;
            vm.Navigate = r => { went = r; return Task.CompletedTask; };
            await vm.RecordCommand.ExecuteAsync(null);
            Assert.Null(went);
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    // ------------------------------------------------------------------ record

    [Fact]
    public async Task Record_validates_amount_category_and_future_date_inline()
    {
        try
        {
            await using var phone = await DeviceAsync(NewPath());
            LocalSubscriptionGate.Set(Biz, Workshop);
            var vm = RecordVm(phone);
            await vm.AppearAsync();
            Assert.True(vm.ShowForm);

            Assert.Null(vm.Validate(out _));
            Assert.NotNull(vm.AmountError);
            Assert.NotNull(vm.CategoryError);
            Assert.Equal("Check the amount, category.", vm.ErrorMessage);

            vm.AmountText = "120";
            vm.SelectCategoryCommand.Execute(ExpenseCategory.Materials);
            vm.SpentOn = vm.MaxDate.AddDays(1);
            Assert.Null(vm.Validate(out _));
            Assert.NotNull(vm.DateError);
            Assert.Null(vm.AmountError);

            vm.SpentOn = vm.MaxDate;
            Assert.NotNull(vm.Validate(out var amount));
            Assert.Equal(120m, amount);
            Assert.Equal(new DateTime(2020, 1, 1), vm.MinDate);
            Assert.Equal("0 / 2000", vm.NoteCounter);
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    [Fact]
    public async Task Record_back_with_unsaved_changes_asks_first()
    {
        await using var phone = await DeviceAsync(NewPath());
        var vm = RecordVm(phone);
        var asked = 0;
        var wentBack = 0;
        vm.Confirm = (title, _, _, _) => { asked++; Assert.Equal(RecordExpenseViewModel.LeaveTitle, title); return Task.FromResult(false); };
        vm.GoBack = () => { wentBack++; return Task.CompletedTask; };

        Assert.False(vm.IsDirty);
        Assert.False(vm.TryHandleBack()); // nothing typed: leave

        vm.AmountText = "50";
        Assert.True(vm.IsDirty);
        Assert.True(vm.TryHandleBack());
        await Task.Delay(50);
        Assert.Equal(1, asked);
        Assert.Equal(0, wentBack); // "Keep editing"

        vm.Confirm = (_, _, _, _) => Task.FromResult(true);
        await vm.CancelAsync();
        Assert.Equal(1, wentBack);
    }

    [Fact]
    public async Task Record_offline_saves_once_with_one_id_and_says_saved_on_this_device()
    {
        try
        {
            await using var phone = await DeviceAsync(NewPath());
            LocalSubscriptionGate.Set(Biz, Workshop);
            var vm = RecordVm(phone);
            var backs = 0;
            vm.GoBack = () => { backs++; return Task.CompletedTask; };
            await vm.AppearAsync();
            vm.AmountText = "1,250.50";
            vm.SelectCategoryCommand.Execute(ExpenseCategory.StaffPay);
            vm.Note = "Ama's weekly pay";

            await Task.WhenAll(vm.SaveCommand.ExecuteAsync(null), vm.SaveCommand.ExecuteAsync(null));
            await vm.SaveCommand.ExecuteAsync(null); // already saved: no second row

            Assert.Equal(RecordExpenseViewModel.SavedOffline, vm.LastToast);
            Assert.Equal(1, backs);
            Assert.False(vm.IsDirty);
            var list = (await Expenses(phone).ListAsync(Biz, new ExpenseListQuery())).Value!;
            var only = Assert.Single(list.Items);
            Assert.Equal(1250.50m, only.Expense.Amount);
            Assert.Equal(vm.Saved!.Id, only.Id);
            Assert.Equal("u1", only.Expense.RecordedByUserId);
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    [Fact]
    public async Task Record_for_a_tailor_or_read_only_shop_is_blocked()
    {
        try
        {
            await using var phone = await DeviceAsync(NewPath());
            LocalSubscriptionGate.Set(Biz, Workshop);
            var tailor = RecordVm(phone, BusinessRole.Tailor);
            await tailor.AppearAsync();
            Assert.Equal(ExpenseRules.ForbiddenMessage, tailor.BlockedMessage);
            Assert.False(tailor.ShowForm);

            LocalSubscriptionGate.Set(Biz, Workshop with { IsReadOnly = true });
            var owner = RecordVm(phone);
            await owner.AppearAsync();
            Assert.Equal(ExpenseAccess.ReadOnlyMessage, owner.BlockedMessage);
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    // ------------------------------------------------------------------ detail + void

    [Fact]
    public async Task Detail_void_needs_a_reason_and_confirmation_then_shows_voided()
    {
        try
        {
            await using var phone = await DeviceAsync(NewPath());
            LocalSubscriptionGate.Set(Biz, Workshop);
            var today = BusinessTime.Today(DateTimeOffset.UtcNow);
            var id = (await Expenses(phone).RecordAsync(Biz, Request(60m, ExpenseCategory.Transport, "Trotro to Makola", today), "u1")).Value!.Id;

            var vm = new ExpenseDetailViewModel(Expenses(phone), new RoleAuth(BusinessRole.Owner));
            var confirms = new List<bool> { false, true };
            vm.Confirm = (_, _, _, _) => { var answer = confirms[0]; confirms.RemoveAt(0); return Task.FromResult(answer); };
            string? toast = null;
            vm.ShowToast = t => { toast = t; return Task.CompletedTask; };
            vm.ApplyQueryAttributes(new Dictionary<string, object> { ["expenseId"] = id });
            await vm.LoadAsync();

            Assert.Equal("GH₵ 60.00", vm.AmountDisplay);
            Assert.StartsWith("Recorded by you · ", vm.RecordedText);
            Assert.True(vm.HasSyncText);
            Assert.True(vm.ShowVoidButton);

            vm.StartVoidCommand.Execute(null);
            vm.VoidReason = "no";
            await vm.ConfirmVoidAsync();
            Assert.NotNull(vm.VoidError);
            Assert.False(vm.IsVoided);

            vm.VoidReason = "Wrong amount";
            await vm.ConfirmVoidAsync(); // "Keep it"
            Assert.False(vm.IsVoided);

            await vm.ConfirmVoidAsync();
            Assert.True(vm.IsVoided);
            Assert.False(vm.ShowVoidButton);
            Assert.Contains("Reason: Wrong amount", vm.VoidedText);
            Assert.Equal("Expense voided on this device", toast);
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    // ------------------------------------------------------------------ reports + sync details

    [Fact]
    public async Task Reports_show_spent_and_loss_and_see_expenses_opens_the_month()
    {
        try
        {
            await using var phone = await DeviceAsync(NewPath());
            LocalSubscriptionGate.Set(Biz, Workshop);
            var today = BusinessTime.Today(DateTimeOffset.UtcNow);
            await Expenses(phone).RecordAsync(Biz, Request(120m, ExpenseCategory.Materials, null, today), "u1");

            var vm = new ReportsViewModel(new RoleAuth(BusinessRole.Owner), phone.GetRequiredService<IReportLocalService>());
            string? went = null;
            vm.Navigate = r => { went = r; return Task.CompletedTask; };
            await vm.AppearAsync();
            vm.Disappear();

            var r = vm.Report!;
            Assert.Equal(120m, r.Spent);
            Assert.True(r.IsLoss);
            Assert.Equal("Loss", r.ProfitLabel);
            Assert.Equal(ReportRules.ProfitNoteFor(true), r.ProfitNote);
            Assert.StartsWith("GH₵ 120.00 more than", ReportsViewModel.SpentChange(r));
            Assert.Contains(r.SpentByCategory, c => c.Category == ExpenseCategory.Materials && c.Total == 120m);

            await vm.SeeExpensesCommand.ExecuteAsync(null);
            Assert.Equal($"ExpensesPage?month={r.Month}", went);
        }
        finally
        {
            LocalSubscriptionGate.Clear();
        }
    }

    [Fact]
    public void Sync_details_say_held_expenses_wait_for_the_workshop_plan()
    {
        var entry = new SyncQueueEntry("q1", SyncEntityType.Expense, "Expense GH₵ 120.00", "New", SyncQueueState.Pending, null, null, false);
        Assert.Equal(SyncItemRow.ExpenseHeldText, new SyncItemRow(entry, expensesHeld: true).Subtitle);
        Assert.Equal("Expense · New · waiting to sync", new SyncItemRow(entry, expensesHeld: false).Subtitle);

        var mismatch = entry with { State = SyncQueueState.Conflict, Code = SyncErrorCodes.ExpenseMismatch };
        var row = new SyncItemRow(mismatch, expensesHeld: false);
        Assert.Equal(("Keep both", "Use theirs"), (row.PrimaryText, row.SecondaryText));
    }

    // ------------------------------------------------------------------ helpers

    private static ExpensesViewModel ListVm(ServiceProvider device, string role, bool free = false) =>
        new(new RoleAuth(role) { Free = free }, Expenses(device)) { IsOnline = () => true };

    private static RecordExpenseViewModel RecordVm(ServiceProvider device, string role = BusinessRole.Owner) =>
        new(Expenses(device), new RoleAuth(role), device.GetRequiredService<IOrderLocalService>())
        {
            IsOnline = () => false,
            ShowToast = _ => Task.CompletedTask,
            GoBack = () => Task.CompletedTask
        };

    private static RecordExpenseRequest Request(decimal amount, string category, string? note, DateOnly spentOn) =>
        new() { Amount = amount, Category = category, Note = note, SpentOn = spentOn };

    private static IExpenseLocalService Expenses(ServiceProvider device) => device.GetRequiredService<IExpenseLocalService>();

    private static string NewPath() => Path.Combine(Path.GetTempPath(), $"needlebase-expui-{Guid.NewGuid():N}.db3");

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

    private sealed class RoleAuth(string role) : NeedleBase.Mobile.Services.IAuthService
    {
        public string Role { get; set; } = role;
        public bool Free { get; set; }

        public Task<bool> IsFreeModeAsync() => Task.FromResult(Free);
        public Task<AuthUserDto?> RegisterBusinessAsync(RegisterBusinessRequest request, CancellationToken cancellationToken = default) => Task.FromResult<AuthUserDto?>(null);
        public Task<AuthUserDto?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) => Task.FromResult<AuthUserDto?>(null);
        public Task<NeedleBase.Mobile.Services.TokenRefreshResult> RefreshAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasStoredTokensAsync() => Task.FromResult(true);
        public Task ClearTokensAsync() => Task.CompletedTask;
        public Task<bool> LogoutAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("t");
        public Task StoreTokensAsync(TokenResponse tokens) => Task.CompletedTask;
        public Task StoreSessionAsync(AuthUserDto user) => Task.CompletedTask;
        public Task<string?> GetBusinessIdAsync() => Task.FromResult<string?>(Biz);
        public Task<AuthUserDto?> GetCachedUserAsync() =>
            Task.FromResult<AuthUserDto?>(new("u1", "owner@ama-stitches.test", "Ama", "Owusu", Biz, "Ama Stitches", Role, null));
    }
}
