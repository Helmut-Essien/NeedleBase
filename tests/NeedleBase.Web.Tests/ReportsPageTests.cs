using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using NeedleBase.Application.Contracts.Reports;
using NeedleBase.Application.Reports;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;
using ReportsIndex = NeedleBase.Web.Pages.Reports.IndexModel;

namespace NeedleBase.Web.Tests;

/// <summary>Plan step 7b: Reports page model (role + plan gating, month handling) and rendered HTML.</summary>
public class ReportsPageModelTests
{
    [Theory]
    [InlineData(BusinessRole.Owner)]
    [InlineData(BusinessRole.Manager)]
    public async Task Owner_and_manager_load_the_report(string role)
    {
        var api = new FakeApi();
        var page = Pages.With(new ReportsIndex(api) { Month = " 2026-11 " }, role);
        var result = await page.OnGetAsync(default);
        Assert.IsType<PageResult>(result);
        Assert.True(page.Loaded);
        Assert.Contains("report:2026-11", api.Calls);
        Assert.Equal(300m, page.Report!.MoneyReceived);
        Assert.False(page.NeedsUpgrade);
    }

    [Fact]
    public async Task Tailor_makes_no_call()
    {
        var api = new FakeApi();
        var page = Pages.With(new ReportsIndex(api), BusinessRole.Tailor);
        await page.OnGetAsync(default);
        Assert.Empty(api.Calls);
        Assert.Equal(ReportRules.ForbiddenMessage, page.PageError);
        Assert.False(page.CanRetry);
    }

    [Fact]
    public async Task Starter_shows_upgrade_not_error()
    {
        var api = new FakeApi { MonthlyReport = _ => Data.Problem<MonthlyReportDto>(HttpStatusCode.Forbidden, ReportRules.NotAllowedCode, ReportRules.NotAllowedMessage) };
        var page = Pages.With(new ReportsIndex(api), BusinessRole.Owner);
        await page.OnGetAsync(default);
        Assert.True(page.NeedsUpgrade);
        Assert.Null(page.PageError);
        Assert.Null(page.Report);
    }

    [Fact]
    public async Task Bad_month_goes_back_to_this_month()
    {
        var api = new FakeApi { MonthlyReport = _ => Data.Problem<MonthlyReportDto>(HttpStatusCode.BadRequest, ReportRules.MonthInvalidCode, ReportRules.MonthInvalidMessage) };
        var page = Pages.With(new ReportsIndex(api) { Month = "2099-01" }, BusinessRole.Owner);
        var result = await page.OnGetAsync(default);
        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Reports/Index", redirect.PageName);
    }

    [Fact]
    public async Task Offline_and_session_end_states()
    {
        var offline = Pages.With(new ReportsIndex(new FakeApi { MonthlyReport = _ => ApiResult<MonthlyReportDto>.Offline() }), BusinessRole.Owner);
        await offline.OnGetAsync(default);
        Assert.True(offline.IsOffline);
        Assert.True(offline.CanRetry);

        var expired = Pages.With(new ReportsIndex(new FakeApi { MonthlyReport = _ => Data.Problem<MonthlyReportDto>(HttpStatusCode.Unauthorized) }), BusinessRole.Owner);
        Assert.IsType<LocalRedirectResult>(await expired.OnGetAsync(default));
    }
}

public class ReportsRenderTests : IClassFixture<RenderTests.WebFactory>
{
    private readonly RenderTests.WebFactory _factory;

    public ReportsRenderTests(RenderTests.WebFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpStatusCode Status, string Html)> GetAsync(string url, string role)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var response = await client.GetAsync(url);
        // Decoded so money (GH₵) and apostrophes read as the user sees them.
        return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Owner_sees_numbers_nav_link_and_costs_note_without_inline_styles()
    {
        var (status, html) = await GetAsync("/Reports", BusinessRole.Owner);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(">Reports</a>", html);
        Assert.Contains("November 2026 (so far)", html);
        Assert.Contains("GH₵ 300.00", html); // money in
        Assert.Contains("GH₵ 500.00", html); // orders taken value (cancelled excluded)
        Assert.Contains("GH₵ 200.00", html); // still owed
        Assert.Contains("GH₵ 150.00 more than October 2026", html);
        Assert.Contains("Mobile Money (1)", html);
        Assert.Contains("not profit after costs", html);
        Assert.Contains("nb-report-bar", html);
        Assert.DoesNotContain("style=\"", html);
        Assert.DoesNotContain("Later", html); // current month: nothing after it
        Assert.Contains(
            $"max=\"{ReportRules.Key(ReportRules.FirstOfMonth(NeedleBase.Application.Common.BusinessTime.Today(DateTimeOffset.UtcNow)))}\"",
            html);
    }

    [Fact]
    public async Task Tailor_has_no_reports_link_or_numbers()
    {
        var (_, dash) = await GetAsync("/Dashboard", BusinessRole.Tailor);
        Assert.DoesNotContain(">Reports</a>", dash);
        var (status, html) = await GetAsync("/Reports", BusinessRole.Tailor);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(ReportRules.ForbiddenMessage, html);
        Assert.DoesNotContain("GH₵", html);
    }

    [Fact]
    public async Task Starter_owner_gets_upgrade_prompt_manager_is_told_to_ask_owner()
    {
        var before = _factory.Api.MonthlyReport;
        try
        {
            _factory.Api.MonthlyReport = _ => Data.Problem<MonthlyReportDto>(HttpStatusCode.Forbidden, ReportRules.NotAllowedCode, ReportRules.NotAllowedMessage);
            var (_, owner) = await GetAsync("/Reports", BusinessRole.Owner);
            Assert.Contains("Monthly reports aren't on your plan", owner);
            Assert.Contains("href=\"/Settings/License\"", owner);
            Assert.DoesNotContain("alert-danger", owner);
            var (_, manager) = await GetAsync("/Reports", BusinessRole.Manager);
            Assert.Contains("Ask the shop owner", manager);
        }
        finally
        {
            _factory.Api.MonthlyReport = before;
        }
    }
}
