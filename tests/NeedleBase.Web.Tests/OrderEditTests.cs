using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;
using Edit = NeedleBase.Web.Pages.Orders.EditModel;

namespace NeedleBase.Web.Tests;

internal static class EditData
{
    public static readonly DateTimeOffset Taken = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    public static OrderLineDto Line(string id, decimal price, bool measured = true) => new(
        id, GarmentTemplate.Dress, 1, price, price, null, MeasurementUnit.Centimetres,
        measured ? new Dictionary<string, decimal?> { ["bust"] = 92m } : new Dictionary<string, decimal?>(),
        null, 0, measured ? Taken : null);

    public static OrderDetailDto Order(string id, params OrderLineDto[] lines) =>
        Data.Detail(id) with { Lines = lines, TotalAmount = lines.Sum(l => l.LineTotal) };
}

public class OrderEditPageTests
{
    private static FakeApi Api(OrderDetailDto order) => new()
    {
        Order = _ => ApiResult<OrderDetailDto>.Ok(order),
        UpdateOrder = (id, r) => ApiResult<OrderDetailDto>.Ok(order)
    };

    [Fact]
    public async Task Tailor_is_forbidden_and_never_calls_edit()
    {
        var api = Api(EditData.Order("o1", EditData.Line("l1", 100m)));
        var page = Pages.With(new Edit(api) { Id = "o1" }, BusinessRole.Tailor);
        await page.OnGetAsync(default);
        Assert.Equal(403, page.Response.StatusCode);
        await page.OnPostAsync(default);
        Assert.DoesNotContain("edit", api.Calls);
    }

    [Fact]
    public async Task Get_fills_form_from_order_with_paid()
    {
        var api = Api(EditData.Order("o1", EditData.Line("l1", 100m)));
        var page = Pages.With(new Edit(api) { Id = "o1" }, BusinessRole.Manager);
        Assert.IsType<PageResult>(await page.OnGetAsync(default));
        Assert.Equal("100.00", Assert.Single(page.Lines).Price);
        Assert.Equal(40m, page.Paid);
    }

    [Fact]
    public async Task Post_sends_lines_and_new_garment_then_redirects()
    {
        var api = Api(EditData.Order("o1", EditData.Line("l1", 100m)));
        var page = Pages.With(new Edit(api)
        {
            Id = "o1",
            BaseModified = Data.Modified.ToString("o"),
            Notes = " hem ",
            Lines = [new Edit.EditLineInput { Id = "l1", Quantity = 2, Price = "GH₵ 1,250.50" }],
            NewGarment = GarmentTemplate.Shirt,
            NewQuantity = 1,
            NewPrice = "150"
        }, BusinessRole.Owner);

        var result = Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(default));
        Assert.Equal("/Orders/Details", result.PageName);
        var sent = api.LastEdit!;
        Assert.Equal(Data.Modified, sent.BaseModifiedUtc);
        Assert.Equal("hem", sent.Notes);
        Assert.Equal(2, sent.Lines!.Count);
        Assert.Equal(1250.50m, sent.Lines[0].UnitPrice);
        Assert.Equal("l1", sent.Lines[0].Id);
        Assert.Null(sent.Lines[1].Id);
        Assert.Equal(GarmentTemplate.Shirt, sent.Lines[1].GarmentTemplate);
    }

    [Fact]
    public async Task Removing_the_last_garment_is_refused_locally()
    {
        var api = Api(EditData.Order("o1", EditData.Line("l1", 100m)));
        var page = Pages.With(new Edit(api)
        {
            Id = "o1",
            Lines = [new Edit.EditLineInput { Id = "l1", Quantity = 1, Price = "100", Remove = true }]
        }, BusinessRole.Owner);
        Assert.IsType<PageResult>(await page.OnPostAsync(default));
        Assert.Contains("at least one garment", page.FormError);
        Assert.DoesNotContain("edit", api.Calls);
    }

    [Fact]
    public async Task Bad_price_is_refused_locally()
    {
        var api = Api(EditData.Order("o1", EditData.Line("l1", 100m)));
        var page = Pages.With(new Edit(api) { Id = "o1", Lines = [new Edit.EditLineInput { Id = "l1", Quantity = 1, Price = "abc" }] }, BusinessRole.Owner);
        await page.OnPostAsync(default);
        Assert.Contains("enter a price", page.FormError);
    }

    [Fact]
    public async Task Below_paid_shows_friendly_error()
    {
        var api = Api(EditData.Order("o1", EditData.Line("l1", 100m)));
        api.UpdateOrder = (_, _) => Data.Problem<OrderDetailDto>(HttpStatusCode.Conflict, OrderErrorMessages.TotalBelowPaid,
            OrderErrorMessages.For(OrderErrorMessages.TotalBelowPaid));
        var page = Pages.With(new Edit(api) { Id = "o1", Lines = [new Edit.EditLineInput { Id = "l1", Quantity = 1, Price = "10" }] }, BusinessRole.Owner);
        Assert.IsType<PageResult>(await page.OnPostAsync(default));
        Assert.False(string.IsNullOrWhiteSpace(page.FormError));
        Assert.DoesNotContain("_", page.FormError);
    }

    [Fact]
    public async Task Stale_reloads_latest_and_explains()
    {
        var api = Api(EditData.Order("o1", EditData.Line("l1", 100m)));
        api.UpdateOrder = (_, _) => Data.Problem<OrderDetailDto>(HttpStatusCode.Conflict, StaleUpdate.Code, "stale");
        var page = Pages.With(new Edit(api) { Id = "o1", Lines = [new Edit.EditLineInput { Id = "l1", Quantity = 3, Price = "10" }] }, BusinessRole.Owner);
        await page.OnPostAsync(default);
        Assert.Equal(Edit.StaleMessage, page.FormError);
        Assert.Equal(1, page.Lines[0].Quantity);
    }

    [Theory]
    [InlineData("150", 150)]
    [InlineData("GH₵ 1,250.50", 1250.50)]
    [InlineData(" ghs 20 ", 20)]
    [InlineData("10,50", 10.50)]
    [InlineData("1,000", 1000)]
    public void ParseMoney_accepts_cedi_forms(string text, decimal expected) => Assert.Equal(expected, Edit.ParseMoney(text));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1.000")]
    [InlineData("1,2,3")]
    public void ParseMoney_rejects_junk(string text) => Assert.Null(Edit.ParseMoney(text));
}

public class OrderEditRenderTests : IClassFixture<RenderTests.WebFactory>
{
    private readonly RenderTests.WebFactory _factory;

    public OrderEditRenderTests(RenderTests.WebFactory factory)
    {
        _factory = factory;
        factory.Api.Order = id => ApiResult<OrderDetailDto>.Ok(id == "old"
            ? EditData.Order(id, EditData.Line("l1", 100m, measured: false))
            : EditData.Order(id, EditData.Line("l1", 100m)));
    }

    private async Task<(HttpStatusCode Status, string Html)> GetAsync(string url, string role)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var response = await client.GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Details_shows_snapshot_and_edit_for_owner()
    {
        var (status, html) = await GetAsync("/Orders/Details/o1", BusinessRole.Owner);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("nb-measure", html);
        Assert.Contains("Taken ", html);
        Assert.Contains("/Orders/Edit/o1", html);
    }

    [Fact]
    public async Task Details_tailor_sees_measurements_but_no_edit()
    {
        var (_, html) = await GetAsync("/Orders/Details/o1", BusinessRole.Tailor);
        Assert.Contains("nb-measure", html);
        Assert.DoesNotContain("/Orders/Edit/o1", html);
    }

    [Fact]
    public async Task Old_order_shows_no_measurements_and_customer_link()
    {
        var (_, html) = await GetAsync("/Orders/Details/old", BusinessRole.Tailor);
        Assert.Contains("No measurements saved with this order", html);
        Assert.Contains("/Customers", html);
    }

    [Theory]
    [InlineData(BusinessRole.Owner, HttpStatusCode.OK)]
    [InlineData(BusinessRole.Manager, HttpStatusCode.OK)]
    [InlineData(BusinessRole.Tailor, HttpStatusCode.Forbidden)]
    public async Task Edit_page_access(string role, HttpStatusCode expected)
    {
        var (status, html) = await GetAsync("/Orders/Edit/o1", role);
        Assert.Equal(expected, status);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Contains("Save changes", html);
            Assert.Contains("Add a garment", html);
            Assert.Contains("name=\"Lines[0].Price\"", html);
        }
    }
}
