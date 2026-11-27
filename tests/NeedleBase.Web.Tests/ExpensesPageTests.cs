using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Expenses;
using NeedleBase.Application.Licensing;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;
using ExpenseCreate = NeedleBase.Web.Pages.Expenses.CreateModel;
using ExpenseDetails = NeedleBase.Web.Pages.Expenses.DetailsModel;
using ExpensesIndex = NeedleBase.Web.Pages.Expenses.IndexModel;
using OrderDetails = NeedleBase.Web.Pages.Orders.DetailsModel;

namespace NeedleBase.Web.Tests;

/// <summary>Expenses plan chunk 8: expense page models (role, plan, read-only, validation, void, order card).</summary>
public class ExpensesPageModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------ list

    [Fact]
    public async Task Owner_lists_the_month_with_names_and_record_button()
    {
        var api = new FakeApi
        {
            Expenses = q => ApiResult<ExpenseListResponse>.Ok(Data.ExpenseList([Data.Expense("e1"), Data.Expense("e2", 80m, ExpenseCategory.RentAndBills)])),
            Staff = () => ApiResult<IReadOnlyList<NeedleBase.Application.Contracts.Staff.StaffNameDto>>.Ok([new("u1", "Ama Owusu", BusinessRole.Owner)])
        };
        var page = Pages.With(new ExpensesIndex(api) { Month = "2026-10", UtcNow = () => Now }, BusinessRole.Manager);
        Assert.IsType<PageResult>(await page.OnGetAsync(default));
        Assert.True(page.Loaded);
        var q = Assert.Single(api.ExpenseQueries);
        Assert.Equal(("2026-10", (string?)null, true), (q.Month, q.OrderId, q.IncludeVoided));
        Assert.Equal("Ama Owusu", page.Who("u1"));
        Assert.Equal("Former team member", page.Who("u-left"));
        Assert.Equal("Not recorded", page.Who(null));
        Assert.Equal("2026-09", page.PreviousMonthKey);
        Assert.Equal("2026-11", page.NextMonthKey);
        Assert.True(page.CanRecord);
        Assert.Equal("Fabric and materials (GH₵ 120.00)", page.TopCategory);
    }

    [Fact]
    public async Task Month_bounds_no_future_and_back_to_jan_2020()
    {
        var future = Pages.With(new ExpensesIndex(new FakeApi()) { Month = "2027-01", UtcNow = () => Now }, BusinessRole.Owner);
        Assert.Equal("/Expenses/Index", Assert.IsType<RedirectToPageResult>(await future.OnGetAsync(default)).PageName);

        var first = Pages.With(new ExpensesIndex(new FakeApi()) { Month = "2020-01", UtcNow = () => Now }, BusinessRole.Owner);
        await first.OnGetAsync(default);
        Assert.Null(first.PreviousMonthKey);

        var current = Pages.With(new ExpensesIndex(new FakeApi()) { UtcNow = () => Now }, BusinessRole.Owner);
        await current.OnGetAsync(default);
        Assert.Equal("2026-11", current.MonthKey);
        Assert.Null(current.NextMonthKey);
    }

    [Fact]
    public async Task Filtered_and_plain_empty_texts_and_order_view()
    {
        var api = new FakeApi();
        var filtered = Pages.With(new ExpensesIndex(api) { Month = "2026-11", Category = ExpenseCategory.StaffPay, UtcNow = () => Now }, BusinessRole.Owner);
        await filtered.OnGetAsync(default);
        Assert.Equal("No staff pay expenses in November 2026. Choose All to see every expense.", filtered.EmptyText);

        var plain = Pages.With(new ExpensesIndex(api) { Category = "Nonsense", UtcNow = () => Now }, BusinessRole.Owner);
        await plain.OnGetAsync(default);
        Assert.Null(plain.Category);
        Assert.StartsWith("No expenses in November 2026.", plain.EmptyText);

        var order = Pages.With(new ExpensesIndex(api) { OrderId = "o1", UtcNow = () => Now }, BusinessRole.Owner);
        await order.OnGetAsync(default);
        Assert.Equal("o1", api.ExpenseQueries.Last().OrderId);
        Assert.Null(api.ExpenseQueries.Last().Month);
        Assert.Equal("Order #ORD-1 · Ama Mensah", order.OrderTitle);
        Assert.Equal("No expenses for this order yet.", order.EmptyText);
    }

    [Fact]
    public async Task Tailor_is_refused_without_calls()
    {
        var api = new FakeApi();
        var list = Pages.With(new ExpensesIndex(api), BusinessRole.Tailor);
        await list.OnGetAsync(default);
        var create = Pages.With(new ExpenseCreate(api), BusinessRole.Tailor);
        await create.OnGetAsync(default);
        var details = Pages.With(new ExpenseDetails(api) { Id = "e1" }, BusinessRole.Tailor);
        await details.OnGetAsync(default);
        Assert.Empty(api.Calls);
        Assert.Equal(ExpenseRules.ForbiddenMessage, list.PageError);
        Assert.Equal(ExpenseRules.ForbiddenMessage, create.PageError);
        Assert.Equal(ExpenseRules.ForbiddenMessage, details.PageError);
        Assert.False(create.ShowForm);
    }

    [Fact]
    public async Task Starter_gets_upgrade_not_an_error()
    {
        var api = new FakeApi
        {
            Expenses = _ => Data.Problem<ExpenseListResponse>(HttpStatusCode.Forbidden, RecordErrorMessages.ExpensesNotAllowed, ExpenseRules.NotAllowedMessage),
            Subscription = () => ApiResult<SubscriptionStatusDto>.Ok(SubscriptionStatusDto.Unlocked with { CanReports = false, PlanCode = PlanCodes.Starter })
        };
        var list = Pages.With(new ExpensesIndex(api) { UtcNow = () => Now }, BusinessRole.Owner);
        await list.OnGetAsync(default);
        Assert.True(list.NeedsUpgrade);
        Assert.Null(list.PageError);
        Assert.Null(list.Result);

        var create = Pages.With(new ExpenseCreate(api) { UtcNow = () => Now }, BusinessRole.Owner);
        await create.OnGetAsync(default);
        Assert.True(create.NeedsUpgrade);
        Assert.False(create.ShowForm);
    }

    [Fact]
    public async Task Read_only_shop_lists_but_hides_record_and_void()
    {
        var api = new FakeApi
        {
            Expenses = _ => ApiResult<ExpenseListResponse>.Ok(Data.ExpenseList([Data.Expense("e1")])),
            Subscription = () => ApiResult<SubscriptionStatusDto>.Ok(SubscriptionStatusDto.Unlocked with { IsReadOnly = true })
        };
        var list = Pages.With(new ExpensesIndex(api) { UtcNow = () => Now }, BusinessRole.Owner);
        await list.OnGetAsync(default);
        Assert.True(list.IsReadOnly);
        Assert.False(list.CanRecord);

        var create = Pages.With(new ExpenseCreate(api) { UtcNow = () => Now }, BusinessRole.Owner);
        await create.OnGetAsync(default);
        Assert.Equal(LicenseErrors.ReadOnlyMessage, create.BlockedMessage);
        Assert.False(create.ShowForm);

        var details = Pages.With(new ExpenseDetails(api) { Id = "e1" }, BusinessRole.Owner);
        await details.OnGetAsync(default);
        Assert.False(details.CanVoid);
    }

    // ------------------------------------------------------------------ create

    [Fact]
    public async Task Create_prefills_order_and_makes_a_client_id()
    {
        var api = new FakeApi();
        var page = Pages.With(new ExpenseCreate(api) { OrderId = "o1", UtcNow = () => Now }, BusinessRole.Owner);
        await page.OnGetAsync(default);
        Assert.True(page.ShowForm);
        Assert.True(page.HasOrder);
        Assert.True(page.FromOrder);
        Assert.Equal("Order #ORD-1 · Ama Mensah", page.OrderLabel);
        Assert.Equal(26, page.ClientId!.Length);
        Assert.Equal("2026-11-27", page.SpentOn);
        Assert.Equal("2026-11-27", page.TodayKey);
        Assert.Equal("2020-01-01", page.EarliestKey);
        Assert.Equal("/Orders/Details/o1", page.BackUrl);
    }

    [Fact]
    public async Task Create_validates_inline_without_calling_the_api()
    {
        var api = new FakeApi();
        var page = Pages.With(new ExpenseCreate(api) { UtcNow = () => Now, ClientId = "01JABCDEFGHJKMNPQRSTVWXYZ0", Amount = "abc", SpentOn = "2026-11-28" }, BusinessRole.Owner);
        Assert.IsType<PageResult>(await page.OnPostAsync(default));
        Assert.Empty(api.RecordedExpenses);
        Assert.NotNull(page.AmountError);
        Assert.NotNull(page.CategoryError);
        Assert.NotNull(page.DateError);
        Assert.Equal("Check the amount, category, date.", page.FormError);
    }

    [Fact]
    public async Task Create_posts_once_with_the_client_id_and_goes_back_to_the_month_or_order()
    {
        var api = new FakeApi();
        var page = Pages.With(new ExpenseCreate(api)
        {
            UtcNow = () => Now, ClientId = "01JABCDEFGHJKMNPQRSTVWXYZ0", Amount = "GH₵ 1,250.50", Category = ExpenseCategory.StaffPay,
            SpentOn = "2026-10-31", Note = "Kofi's pay"
        }, BusinessRole.Owner);
        var redirect = Assert.IsType<RedirectResult>(await page.OnPostAsync(default));
        Assert.Equal("/Expenses?month=2026-10", redirect.Url);
        var sent = Assert.Single(api.RecordedExpenses);
        Assert.Equal(("01JABCDEFGHJKMNPQRSTVWXYZ0", 1250.50m, ExpenseCategory.StaffPay, new DateOnly(2026, 10, 31)), (sent.Id, sent.Amount, sent.Category, sent.SpentOn!.Value));
        Assert.Equal(ExpenseWeb.FlashRecorded, page.Flash);

        var fromOrder = Pages.With(new ExpenseCreate(api)
        {
            UtcNow = () => Now, ClientId = "01JABCDEFGHJKMNPQRSTVWXYZ1", Amount = "60", Category = ExpenseCategory.Materials,
            SpentOn = "2026-11-27", OrderId = "o1", FromOrder = true
        }, BusinessRole.Manager);
        Assert.Equal("/Orders/Details/o1", Assert.IsType<RedirectResult>(await fromOrder.OnPostAsync(default)).Url);
        Assert.Equal("o1", api.RecordedExpenses.Last().OrderId);
    }

    [Fact]
    public async Task Create_maps_api_refusals()
    {
        var api = new FakeApi { RecordExpense = _ => Data.Problem<ExpenseDto>(HttpStatusCode.BadRequest, RecordErrorMessages.ExpenseOrderInvalid, "That order isn't in this shop.") };
        var page = Pages.With(new ExpenseCreate(api) { UtcNow = () => Now, ClientId = "01JABCDEFGHJKMNPQRSTVWXYZ0", Amount = "60", Category = ExpenseCategory.Transport, SpentOn = "2026-11-27", OrderId = "o9" }, BusinessRole.Owner);
        Assert.IsType<PageResult>(await page.OnPostAsync(default));
        Assert.Equal("That order isn't in this shop.", page.OrderError);
        Assert.Null(page.OrderId);

        api.RecordExpense = _ => Data.Problem<ExpenseDto>(HttpStatusCode.Forbidden, LicenseErrors.ReadOnly, LicenseErrors.ReadOnlyMessage);
        var ro = Pages.With(new ExpenseCreate(api) { UtcNow = () => Now, ClientId = "01JABCDEFGHJKMNPQRSTVWXYZ0", Amount = "60", Category = ExpenseCategory.Transport, SpentOn = "2026-11-27" }, BusinessRole.Owner);
        await ro.OnPostAsync(default);
        Assert.Equal(LicenseErrors.ReadOnlyMessage, ro.FormError);

        api.RecordExpense = _ => Data.Problem<ExpenseDto>(HttpStatusCode.Unauthorized);
        var expired = Pages.With(new ExpenseCreate(api) { UtcNow = () => Now, ClientId = "x", Amount = "60", Category = ExpenseCategory.Transport, SpentOn = "2026-11-27" }, BusinessRole.Owner);
        Assert.IsType<LocalRedirectResult>(await expired.OnPostAsync(default));
    }

    // ------------------------------------------------------------------ details + void

    [Fact]
    public async Task Details_shows_order_removed_and_recorder()
    {
        var api = new FakeApi { Expense = id => ApiResult<ExpenseDto>.Ok(Data.Expense(id, orderId: "o-gone")) };
        var page = Pages.With(new ExpenseDetails(api) { Id = "e1" }, BusinessRole.Owner);
        await page.OnGetAsync(default);
        Assert.Equal("Order removed", page.OrderLine);
        Assert.True(page.CanVoid);
        Assert.Contains(" · ", page.RecordedText);

        var missing = Pages.With(new ExpenseDetails(new FakeApi { Expense = _ => Data.Problem<ExpenseDto>(HttpStatusCode.NotFound, RecordErrorMessages.ExpenseNotFound) }) { Id = "e9" }, BusinessRole.Owner);
        await missing.OnGetAsync(default);
        Assert.False(missing.CanRetry);
        Assert.Contains("wasn't found", missing.PageError);
    }

    [Fact]
    public async Task Void_needs_a_reason_and_the_confirm_box()
    {
        var api = new FakeApi();
        var page = Pages.With(new ExpenseDetails(api) { Id = "e1", VoidReason = "no", ConfirmVoid = false }, BusinessRole.Owner);
        Assert.IsType<PageResult>(await page.OnPostVoidAsync(default));
        Assert.Equal(ExpenseRules.VoidReasonMessage, page.VoidError);
        Assert.Equal(ExpenseDetails.ConfirmMessage, page.ConfirmError);
        Assert.DoesNotContain(api.Calls, c => c.StartsWith("void-expense", StringComparison.Ordinal));

        var ok = Pages.With(new ExpenseDetails(api) { Id = "e1", VoidReason = " Wrong amount ", ConfirmVoid = true, BaseModified = Data.Modified.ToString("o") }, BusinessRole.Owner);
        Assert.IsType<RedirectToPageResult>(await ok.OnPostVoidAsync(default));
        Assert.Contains("void-expense:e1", api.Calls);
        Assert.Equal(ExpenseWeb.FlashVoided, ok.Flash);
    }

    [Fact]
    public async Task Void_stale_shows_the_fresh_copy()
    {
        var api = new FakeApi { VoidExpense = (_, _) => Data.Problem<ExpenseDto>(HttpStatusCode.Conflict, NeedleBase.Application.Common.StaleUpdate.Code) };
        var page = Pages.With(new ExpenseDetails(api) { Id = "e1", VoidReason = "Wrong amount", ConfirmVoid = true }, BusinessRole.Owner);
        Assert.IsType<PageResult>(await page.OnPostVoidAsync(default));
        Assert.Equal(ExpenseDetails.StaleMessage, page.VoidError);
        Assert.NotNull(page.Expense);
    }

    // ------------------------------------------------------------------ order card

    [Fact]
    public async Task Order_card_for_owner_hidden_for_tailor_and_starter()
    {
        var api = new FakeApi { Expenses = q => ApiResult<ExpenseListResponse>.Ok(Data.ExpenseList([Data.Expense("e1", orderId: q.OrderId, orderRef: "ORD-1")], null)) };
        var owner = Pages.With(new OrderDetails(api) { Id = "o1" }, BusinessRole.Owner);
        await owner.OnGetAsync(default);
        Assert.NotNull(owner.Expenses);
        Assert.True(owner.CanAddExpense);
        Assert.Equal(("o1", OrderDetails.ExpensePreviewCount), (api.ExpenseQueries.Last().OrderId, api.ExpenseQueries.Last().PageSize));

        var tailorApi = new FakeApi();
        var tailor = Pages.With(new OrderDetails(tailorApi) { Id = "o1" }, BusinessRole.Tailor);
        await tailor.OnGetAsync(default);
        Assert.Null(tailor.Expenses);
        Assert.Empty(tailorApi.ExpenseQueries);

        var starter = Pages.With(new OrderDetails(new FakeApi
        {
            Expenses = _ => Data.Problem<ExpenseListResponse>(HttpStatusCode.Forbidden, RecordErrorMessages.ExpensesNotAllowed)
        }) { Id = "o1" }, BusinessRole.Owner);
        await starter.OnGetAsync(default);
        Assert.Null(starter.Expenses);
        Assert.True(starter.Loaded); // the order still shows
    }
}

public class ExpensesRenderTests : IClassFixture<RenderTests.WebFactory>
{
    private readonly RenderTests.WebFactory _factory;

    public ExpensesRenderTests(RenderTests.WebFactory factory)
    {
        _factory = factory;
        factory.Api.Expenses = q => ApiResult<ExpenseListResponse>.Ok(Data.ExpenseList(
        [
            Data.Expense("e1", orderId: "o1", orderRef: "ORD-1"),
            Data.Expense("e2", 80m, ExpenseCategory.RentAndBills) with { IsVoided = true, VoidReason = "Typed twice" }
        ], q.OrderId is null ? q.Month : null));
    }

    private async Task<(HttpStatusCode Status, string Html)> GetAsync(string url, string role)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var response = await client.GetAsync(url);
        return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Owner_sees_list_nav_link_rows_and_no_inline_styles()
    {
        var (status, html) = await GetAsync("/Expenses", BusinessRole.Owner);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(">Expenses</a>", html);
        Assert.Contains("20 Nov 2026", html);
        Assert.Contains("GH₵ 120.00", html);
        Assert.Contains("Lining and zips", html);
        Assert.DoesNotContain("from Makola", html); // first line of the note only
        Assert.Contains("#ORD-1 · Ama Mensah", html);
        Assert.Contains("Voided", html);
        Assert.Contains("Record expense", html);
        Assert.Contains("Include voided", html);
        Assert.DoesNotContain("style=\"", html);
    }

    [Fact]
    public async Task Create_and_details_render_with_antiforgery_and_prefilled_order()
    {
        var (status, create) = await GetAsync("/Expenses/Create?orderId=o1", BusinessRole.Owner);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("__RequestVerificationToken", create);
        Assert.Contains("Order #ORD-1 · Ama Mensah", create);
        Assert.Contains("Remove link", create);
        Assert.Contains("type=\"date\"", create);
        Assert.Contains("min=\"2020-01-01\"", create);
        Assert.Contains("maxlength=\"2000\"", create);

        var (_, details) = await GetAsync("/Expenses/Details/e1", BusinessRole.Manager);
        Assert.Contains("Void expense", details);
        Assert.Contains("I understand this stays in the records", details);
        Assert.DoesNotContain("style=\"", details);
    }

    [Fact]
    public async Task Tailor_has_no_link_and_no_numbers()
    {
        var (_, dash) = await GetAsync("/Dashboard", BusinessRole.Tailor);
        Assert.DoesNotContain(">Expenses</a>", dash);
        var (_, html) = await GetAsync("/Expenses", BusinessRole.Tailor);
        Assert.Contains(ExpenseRules.ForbiddenMessage, html);
        Assert.DoesNotContain("GH₵", html);
    }

    [Fact]
    public async Task Order_details_show_the_expenses_card()
    {
        var (_, html) = await GetAsync("/Orders/Details/o1", BusinessRole.Owner);
        Assert.Contains("Expenses for this order", html);
        Assert.Contains("href=\"/Expenses?orderId=o1\"", html);
        Assert.Contains("href=\"/Expenses/Create?orderId=o1\"", html);
    }
}
