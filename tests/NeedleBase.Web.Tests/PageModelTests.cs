using NeedleBase.Application.Common;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;
using Dashboard = NeedleBase.Web.Pages.IndexModel;
using Details = NeedleBase.Web.Pages.Orders.DetailsModel;
using OrdersIndex = NeedleBase.Web.Pages.Orders.IndexModel;
using Production = NeedleBase.Web.Pages.Production.IndexModel;
using PaymentsIndex = NeedleBase.Web.Pages.Payments.IndexModel;

namespace NeedleBase.Web.Tests;

public class DashboardPageTests
{
    [Fact]
    public async Task Owner_loads_summary_due_list_and_payments()
    {
        var api = new FakeApi();
        var page = Pages.With(new Dashboard(api), BusinessRole.Owner);
        Assert.IsType<PageResult>(await page.OnGetAsync(default));
        Assert.True(page.Loaded && page.ShowMoney);
        Assert.Contains("payments", api.Calls);
        var q = Assert.Single(api.OrderQueries);
        Assert.Equal((OrderListSort.OpenFilter, OrderListSort.Due), (q.Status, q.Sort));
    }

    [Fact]
    public async Task Tailor_never_requests_payments()
    {
        var api = new FakeApi { Dashboard = () => ApiResult<NeedleBase.Application.Contracts.Dashboard.DashboardSummaryDto>.Ok(Data.Summary(false)) };
        var page = Pages.With(new Dashboard(api), BusinessRole.Tailor);
        await page.OnGetAsync(default);
        Assert.DoesNotContain("payments", api.Calls);
        Assert.False(page.ShowMoney);
    }

    [Fact]
    public async Task Api_decides_money_visibility_even_for_owner_cookie()
    {
        var api = new FakeApi { Dashboard = () => ApiResult<NeedleBase.Application.Contracts.Dashboard.DashboardSummaryDto>.Ok(Data.Summary(false)) };
        var page = Pages.With(new Dashboard(api), BusinessRole.Owner);
        await page.OnGetAsync(default);
        Assert.False(page.ShowMoney);
    }

    [Fact]
    public async Task Offline_shows_offline_state()
    {
        var api = new FakeApi { Dashboard = ApiResult<NeedleBase.Application.Contracts.Dashboard.DashboardSummaryDto>.Offline };
        var page = Pages.With(new Dashboard(api), BusinessRole.Owner);
        Assert.IsType<PageResult>(await page.OnGetAsync(default));
        Assert.True(page.IsOffline);
        Assert.False(page.Loaded);
        Assert.Null(page.Summary);
    }

    [Fact]
    public async Task Unauthorized_redirects_to_login_with_local_return_url()
    {
        var api = new FakeApi { Dashboard = () => Data.Problem<NeedleBase.Application.Contracts.Dashboard.DashboardSummaryDto>(HttpStatusCode.Unauthorized) };
        var page = Pages.With(new Dashboard(api), BusinessRole.Owner, "/", "?x=1");
        var redirect = Assert.IsType<LocalRedirectResult>(await page.OnGetAsync(default));
        Assert.Equal("/Account/Login?returnUrl=%2F%3Fx%3D1", redirect.Url);
    }

    [Fact]
    public async Task Secondary_failure_keeps_page_loaded()
    {
        var api = new FakeApi { Orders = _ => ApiResult<NeedleBase.Application.Common.PaginatedResponse<OrderListItemDto>>.Offline() };
        var page = Pages.With(new Dashboard(api), BusinessRole.Manager);
        await page.OnGetAsync(default);
        Assert.True(page.Loaded);
        Assert.Empty(page.DueNext);
    }
}

public class OrdersPageTests
{
    [Fact]
    public async Task Filters_normalised_and_unknown_status_dropped()
    {
        var api = new FakeApi();
        var page = Pages.With(new OrdersIndex(api) { Search = "  " + new string('a', 150), Status = "Bogus", PageNumber = -4 }, BusinessRole.Tailor);
        await page.OnGetAsync(default);
        var q = Assert.Single(api.OrderQueries);
        Assert.Equal(100, q.Search!.Length);
        Assert.Null(q.Status);
        Assert.Equal(1, q.Page);
    }

    [Theory]
    [InlineData("me", "me")]
    [InlineData("u2", null)]
    [InlineData(null, null)]
    public async Task Assigned_filter_only_accepts_me(string? assigned, string? sent)
    {
        var api = new FakeApi();
        var page = Pages.With(new OrdersIndex(api) { Assigned = assigned }, BusinessRole.Tailor);
        await page.OnGetAsync(default);
        Assert.Equal(sent, Assert.Single(api.OrderQueries).AssignedTo);
        Assert.Equal(sent is not null, page.Filtered);
    }

    [Fact]
    public async Task Page_past_end_is_flagged_not_empty()
    {
        var api = new FakeApi { Orders = _ => ApiResult<NeedleBase.Application.Common.PaginatedResponse<OrderListItemDto>>.Ok(Data.Page<OrderListItemDto>([], total: 5, page: 9)) };
        var page = Pages.With(new OrdersIndex(api) { PageNumber = 9 }, BusinessRole.Owner);
        await page.OnGetAsync(default);
        Assert.True(page.PastEnd);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("Sewing")]
    public async Task Known_status_passed_through(string status)
    {
        var api = new FakeApi();
        await Pages.With(new OrdersIndex(api) { Status = status }, BusinessRole.Owner).OnGetAsync(default);
        Assert.Equal(status, Assert.Single(api.OrderQueries).Status);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData(" ab ", "ab")]
    [InlineData("abcdef", "abc")]
    public void Normalize(string? input, string? expected) => Assert.Equal(expected, OrdersIndex.Normalize(input, 3));

    [Fact]
    public async Task Forbidden_without_detail_gets_plain_copy()
    {
        var api = new FakeApi { Orders = _ => Data.Problem<NeedleBase.Application.Common.PaginatedResponse<OrderListItemDto>>(HttpStatusCode.Forbidden) };
        var page = Pages.With(new OrdersIndex(api), BusinessRole.Tailor);
        await page.OnGetAsync(default);
        Assert.Equal("You don't have access to this.", page.PageError);
    }
}

public class OrderDetailsPageTests
{
    [Fact]
    public async Task Tailor_does_not_load_payments()
    {
        var api = new FakeApi();
        var page = Pages.With(new Details(api) { Id = "o1" }, BusinessRole.Tailor);
        await page.OnGetAsync(default);
        Assert.DoesNotContain("orderPayments", api.Calls);
        Assert.Null(page.Payments);
        Assert.True(page.Loaded);
    }

    [Fact]
    public async Task Owner_loads_payments_and_base_modified()
    {
        var api = new FakeApi();
        var page = Pages.With(new Details(api) { Id = "o1" }, BusinessRole.Owner);
        await page.OnGetAsync(default);
        Assert.NotNull(page.Payments);
        Assert.Equal(Data.Modified.ToString("o"), page.BaseModified);
    }

    [Fact]
    public async Task Payments_forbidden_just_hides_section()
    {
        var api = new FakeApi { OrderPayments = () => Data.Problem<OrderPaymentsDto>(HttpStatusCode.Forbidden, "PAYMENT_FORBIDDEN") };
        var page = Pages.With(new Details(api) { Id = "o1" }, BusinessRole.Manager);
        await page.OnGetAsync(default);
        Assert.Null(page.Payments);
        Assert.Null(page.PageError);
    }

    [Theory]
    [InlineData("u2", true, "Kofi Boateng")]
    [InlineData("gone", true, "Former team member")]
    [InlineData("u2", false, "Assigned")]
    public async Task Assignee_name_resolution(string assignee, bool staffOk, string expected)
    {
        var api = new FakeApi
        {
            Order = id => ApiResult<OrderDetailDto>.Ok(Data.Detail(id, assignee: assignee)),
            Staff = () => staffOk
                ? ApiResult<IReadOnlyList<StaffNameDto>>.Ok([new StaffNameDto("u2", "Kofi Boateng", "Tailor")])
                : ApiResult<IReadOnlyList<StaffNameDto>>.Offline()
        };
        var page = Pages.With(new Details(api) { Id = "o1" }, BusinessRole.Owner);
        await page.OnGetAsync(default);
        Assert.Equal(expected, page.AssigneeName);
    }

    [Fact]
    public async Task Not_found_message()
    {
        var api = new FakeApi { Order = _ => Data.Problem<OrderDetailDto>(HttpStatusCode.NotFound) };
        var page = Pages.With(new Details(api) { Id = "zz" }, BusinessRole.Owner);
        Assert.IsType<PageResult>(await page.OnGetAsync(default));
        Assert.Contains("wasn't found", page.PageError);
        Assert.False(page.CanChangeStatus);
    }

    [Theory]
    [InlineData("Collected", false)]
    [InlineData("Cancelled", false)]
    [InlineData("Fitting", true)]
    public async Task Terminal_orders_hide_status_form(string status, bool can)
    {
        var api = new FakeApi { Order = id => ApiResult<OrderDetailDto>.Ok(Data.Detail(id, status)) };
        var page = Pages.With(new Details(api) { Id = "o1" }, BusinessRole.Owner);
        await page.OnGetAsync(default);
        Assert.Equal(can, page.CanChangeStatus);
        Assert.DoesNotContain(status, page.StatusChoices);
    }

    [Fact]
    public async Task Successful_post_sends_base_modified_and_redirects()
    {
        var api = new FakeApi();
        var page = Pages.With(new Details(api) { Id = "o1", NewStatus = "Sewing", BaseModified = Data.Modified.ToString("o") }, BusinessRole.Tailor);
        var result = Assert.IsType<RedirectToPageResult>(await page.OnPostStatusAsync(default));
        Assert.Equal(Data.Modified, api.LastUpdate!.BaseModifiedUtc);
        Assert.Equal("Sewing", api.LastUpdate.Status);
        Assert.Equal("Status changed to Sewing.", page.Flash);
        Assert.Equal("o1", result.RouteValues!["id"]);
    }

    [Fact]
    public async Task Stale_post_shows_stale_message_and_reloads_latest()
    {
        var api = new FakeApi { UpdateStatus = (_, _) => Data.Problem<OrderDetailDto>(HttpStatusCode.Conflict, StaleUpdate.Code, "x") };
        var page = Pages.With(new Details(api) { Id = "o1", NewStatus = "Ready", BaseModified = "garbage" }, BusinessRole.Owner);
        Assert.IsType<PageResult>(await page.OnPostStatusAsync(default));
        Assert.Equal(Details.StaleMessage, page.StatusError);
        Assert.Null(api.LastUpdate!.BaseModifiedUtc); // unparsable → not sent
        Assert.NotNull(page.Order);
        Assert.Equal(Data.Modified.ToString("o"), page.BaseModified); // the fresh version
    }

    [Fact]
    public async Task Double_submit_stale_but_already_at_target_is_success_copy()
    {
        var api = new FakeApi
        {
            UpdateStatus = (_, _) => Data.Problem<OrderDetailDto>(HttpStatusCode.Conflict, StaleUpdate.Code),
            Order = id => ApiResult<OrderDetailDto>.Ok(Data.Detail(id, "Ready"))
        };
        var page = Pages.With(new Details(api) { Id = "o1", NewStatus = "Ready" }, BusinessRole.Owner);
        await page.OnPostStatusAsync(default);
        Assert.Null(page.StatusError);
        Assert.Equal($"Status is {OrderStatus.DisplayName("Ready")}.", page.StatusMessage);
    }

    [Fact]
    public async Task Unknown_status_never_reaches_api()
    {
        var api = new FakeApi();
        var page = Pages.With(new Details(api) { Id = "o1", NewStatus = "Teleported" }, BusinessRole.Owner);
        await page.OnPostStatusAsync(default);
        Assert.DoesNotContain("status", api.Calls);
        Assert.Equal("Choose a status.", page.StatusError);
    }

    [Fact]
    public async Task Unauthorized_post_redirects_to_login()
    {
        var api = new FakeApi { UpdateStatus = (_, _) => Data.Problem<OrderDetailDto>(HttpStatusCode.Unauthorized) };
        var page = Pages.With(new Details(api) { Id = "o1", NewStatus = "Ready" }, BusinessRole.Owner, "/Orders/o1");
        Assert.IsType<LocalRedirectResult>(await page.OnPostStatusAsync(default));
    }

    [Fact]
    public void Error_mapping()
    {
        Assert.Equal(ApiResult<int>.OfflineMessage, Details.StatusErrorFor(ApiResult<int>.Offline()));
        Assert.Equal(Details.StaleMessage, Details.StatusErrorFor(Data.Problem<int>(HttpStatusCode.Conflict, StaleUpdate.Code)));
        Assert.Equal("Server says no.", Details.StatusErrorFor(Data.Problem<int>(HttpStatusCode.BadRequest, "SOME_NEW_CODE", "Server says no.")));
        Assert.Equal("Server says no.", Details.StatusErrorFor(Data.Problem<int>(HttpStatusCode.BadRequest, null, "Server says no.")));
    }
}

public class ProductionPageTests
{
    [Fact]
    public async Task Tailor_defaults_to_mine_and_loads_five_columns()
    {
        var api = new FakeApi();
        var page = Pages.With(new Production(api), BusinessRole.Tailor);
        await page.OnGetAsync(default);
        Assert.True(page.MineOnly);
        Assert.Equal(5, api.OrderQueries.Count);
        Assert.All(api.OrderQueries, q => Assert.Equal(("me", OrderListSort.Due, Production.ColumnSize), (q.AssignedTo, q.Sort, q.PageSize)));
        Assert.Equal(Production.Columns.OrderBy(s => s), api.OrderQueries.Select(q => q.Status!).OrderBy(s => s));
    }

    [Theory]
    [InlineData(BusinessRole.Owner, null, false)]
    [InlineData(BusinessRole.Owner, "me", true)]
    [InlineData(BusinessRole.Tailor, "all", false)]
    [InlineData(BusinessRole.Tailor, "weird", true)]
    public async Task Show_switch(string role, string? show, bool mine)
    {
        var page = Pages.With(new Production(new FakeApi()) { Show = show }, role);
        await page.OnGetAsync(default);
        Assert.Equal(mine, page.MineOnly);
    }

    [Fact]
    public async Task Cards_show_assignee_and_hidden_count()
    {
        var api = new FakeApi
        {
            Orders = q => ApiResult<NeedleBase.Application.Common.PaginatedResponse<OrderListItemDto>>.Ok(q.Status == "Sewing"
                ? Data.Page<OrderListItemDto>([Data.Item("1", "Sewing", "u2"), Data.Item("2", "Sewing", "gone"), Data.Item("3", "Sewing")], total: 40, size: 30)
                : Data.Page<OrderListItemDto>([])),
            Staff = () => ApiResult<IReadOnlyList<StaffNameDto>>.Ok([new StaffNameDto("u2", "Kofi Boateng", "Tailor")])
        };
        var page = Pages.With(new Production(api), BusinessRole.Owner);
        await page.OnGetAsync(default);
        var sewing = page.Board.Single(c => c.Status == "Sewing");
        Assert.Equal(["Kofi Boateng", "Former team member", null], sewing.Cards.Select(c => c.AssigneeName));
        Assert.Equal(37, sewing.Hidden);
    }

    [Fact]
    public async Task Any_column_failure_shows_error_not_partial_board()
    {
        var api = new FakeApi { Orders = q => q.Status == "Ready" ? ApiResult<NeedleBase.Application.Common.PaginatedResponse<OrderListItemDto>>.Offline() : ApiResult<NeedleBase.Application.Common.PaginatedResponse<OrderListItemDto>>.Ok(Data.Page<OrderListItemDto>([])) };
        var page = Pages.With(new Production(api), BusinessRole.Owner);
        await page.OnGetAsync(default);
        Assert.True(page.IsOffline);
        Assert.Empty(page.Board);
    }
}

public class RoleGatingTests
{
    [Theory]
    [InlineData(BusinessRole.Owner, true)]
    [InlineData(BusinessRole.Manager, true)]
    [InlineData(BusinessRole.Tailor, false)]
    [InlineData(null, false)]
    [InlineData("owner", false)]
    public void Payments_visibility(string? role, bool expected)
    {
        var page = Pages.With(new Dashboard(new FakeApi()), role);
        Assert.Equal(expected, page.User.CanSeePayments());
    }

    [Fact]
    public async Task Tailor_payments_page_makes_no_call()
    {
        var api = new FakeApi();
        var page = Pages.With(new PaymentsIndex(api), BusinessRole.Tailor);
        await page.OnGetAsync(default);
        Assert.DoesNotContain("payments", api.Calls);
        Assert.NotNull(page.PageError);
    }
}

public class DashboardDefenceTests
{
    [Fact]
    public async Task Tailor_cookie_never_shows_money_even_if_api_sends_it()
    {
        var page = Pages.With(new Dashboard(new FakeApi()), BusinessRole.Tailor); // fake returns Money
        await page.OnGetAsync(default);
        Assert.False(page.ShowMoney);
    }
}

public class ActivityPageTests
{
    [Fact]
    public async Task Filters_are_normalised_and_dates_swapped_when_reversed()
    {
        var api = new FakeApi();
        var page = Pages.With(new NeedleBase.Web.Pages.Activity.IndexModel(api), BusinessRole.Owner);
        page.Kind = "not-a-group";
        page.From = "2026-10-20";
        page.To = "2026-10-01";
        page.Search = "  #ORD-12  ";
        await page.OnGetAsync(default);

        var q = api.LastActivityQuery!;
        Assert.Null(q.Action);
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 20)), (q.From, q.To));
        Assert.Equal("#ORD-12", q.Search);
        Assert.NotNull(page.DateError);
        Assert.Contains("staff", api.Calls);
    }

    [Fact]
    public async Task Manager_cannot_pick_security_events()
    {
        var api = new FakeApi();
        var page = Pages.With(new NeedleBase.Web.Pages.Activity.IndexModel(api), BusinessRole.Manager);
        page.Kind = "auth.";
        await page.OnGetAsync(default);
        Assert.Null(api.LastActivityQuery!.Action);
        Assert.DoesNotContain(page.Groups, g => g.Value == "auth.");

        var owner = Pages.With(new NeedleBase.Web.Pages.Activity.IndexModel(new FakeApi()), BusinessRole.Owner);
        Assert.Contains(owner.Groups, g => g.Value == "auth.");
    }

    [Fact]
    public async Task Tailor_gets_a_message_and_no_api_call()
    {
        var api = new FakeApi();
        var page = Pages.With(new NeedleBase.Web.Pages.Activity.IndexModel(api), BusinessRole.Tailor);
        await page.OnGetAsync(default);
        Assert.DoesNotContain("activity", api.Calls);
        Assert.NotNull(page.PageError);
    }

    [Fact]
    public async Task Order_details_loads_history_for_managers_only()
    {
        var api = new FakeApi();
        var manager = Pages.With(new Details(api), BusinessRole.Manager);
        manager.Id = "o1";
        await manager.OnGetAsync(default);
        Assert.Equal("o1", api.LastActivityQuery!.OrderId);
        Assert.NotNull(manager.History);

        var tailorApi = new FakeApi();
        var tailor = Pages.With(new Details(tailorApi), BusinessRole.Tailor);
        tailor.Id = "o1";
        await tailor.OnGetAsync(default);
        Assert.DoesNotContain("activity", tailorApi.Calls);
        Assert.Null(tailor.History);
    }

    [Fact]
    public async Task History_failure_does_not_break_the_order_page()
    {
        var api = new FakeApi { Activity = _ => ApiResult<PaginatedResponse<NeedleBase.Application.Contracts.Audit.AuditEventDto>>.Offline() };
        var page = Pages.With(new Details(api), BusinessRole.Owner);
        page.Id = "o1";
        await page.OnGetAsync(default);
        Assert.True(page.Loaded);
        Assert.Null(page.History);
    }
}
