using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;
using Details = NeedleBase.Web.Pages.Orders.DetailsModel;

namespace NeedleBase.Web.Tests;

/// <summary>Code-gaps G14: assign an order to a team member from Order detail.</summary>
public class OrderAssignPageTests
{
    private static readonly StaffNameDto Kofi = new("u-kofi", "Kofi Asante", BusinessRole.Tailor);

    private static FakeApi Api(string? assignee = null) => new()
    {
        Order = id => ApiResult<OrderDetailDto>.Ok(Data.Detail(id, assignee: assignee)),
        Staff = () => ApiResult<IReadOnlyList<StaffNameDto>>.Ok([Kofi])
    };

    [Theory]
    [InlineData(BusinessRole.Owner, true)]
    [InlineData(BusinessRole.Manager, true)]
    [InlineData(BusinessRole.Tailor, false)]
    public async Task Picker_shows_for_owner_and_manager_only(string role, bool shown)
    {
        var page = Pages.With(new Details(Api("u-kofi")) { Id = "o1" }, role);
        await page.OnGetAsync(default);
        Assert.Equal(shown, page.CanEdit);
        Assert.Equal("Kofi Asante", page.AssigneeName); // everyone sees who it's assigned to
        Assert.Equal("u-kofi", page.NewAssignee);
        Assert.False(page.AssigneeIsFormer);
    }

    [Fact]
    public async Task Assign_sends_base_and_redirects_with_name()
    {
        var api = Api();
        var page = Pages.With(new Details(api) { Id = "o1", NewAssignee = "u-kofi", BaseModified = Data.Modified.ToString("o") }, BusinessRole.Owner);
        var result = Assert.IsType<RedirectToPageResult>(await page.OnPostAssignAsync(default));
        Assert.Equal("u-kofi", api.LastAssign!.AssignedToUserId);
        Assert.Equal(Data.Modified, api.LastAssign.BaseModifiedUtc);
        Assert.Equal("Order assigned to Kofi Asante.", page.Flash);
        Assert.Equal("o1", result.RouteValues!["id"]);
    }

    [Fact]
    public async Task Not_assigned_choice_unassigns()
    {
        var api = Api("u-kofi");
        var page = Pages.With(new Details(api) { Id = "o1", NewAssignee = "" }, BusinessRole.Manager);
        Assert.IsType<RedirectToPageResult>(await page.OnPostAssignAsync(default));
        Assert.Null(api.LastAssign!.AssignedToUserId);
        Assert.Equal("This order is not assigned to anyone now.", page.Flash);
    }

    [Fact]
    public async Task Tailor_post_is_refused_without_calling_the_api()
    {
        var api = Api();
        var page = Pages.With(new Details(api) { Id = "o1", NewAssignee = "u-kofi" }, BusinessRole.Tailor);
        Assert.IsType<PageResult>(await page.OnPostAssignAsync(default));
        Assert.Null(api.LastAssign);
        Assert.Equal("Only the owner or a manager can assign orders.", page.AssignError);
    }

    [Theory]
    [InlineData(OrderErrorMessages.AssigneeInvalid)]
    [InlineData("SUBSCRIPTION_READ_ONLY")]
    public async Task Api_refusal_shows_plain_error(string code)
    {
        var api = Api();
        api.UpdateAssignee = (_, _) => Data.Problem<OrderDetailDto>(code == OrderErrorMessages.AssigneeInvalid ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden,
            code, "Your shop is read-only until the licence is renewed.");
        var page = Pages.With(new Details(api) { Id = "o1", NewAssignee = "u-kofi" }, BusinessRole.Owner);
        Assert.IsType<PageResult>(await page.OnPostAssignAsync(default));
        Assert.False(string.IsNullOrWhiteSpace(page.AssignError));
        if (code == OrderErrorMessages.AssigneeInvalid)
        {
            Assert.Equal(OrderErrorMessages.For(code), page.AssignError);
        }
    }

    [Fact]
    public async Task Stale_assign_explains_and_reloads()
    {
        var api = Api();
        api.UpdateAssignee = (_, _) => Data.Problem<OrderDetailDto>(HttpStatusCode.Conflict, StaleUpdate.Code);
        var page = Pages.With(new Details(api) { Id = "o1", NewAssignee = "u-kofi" }, BusinessRole.Owner);
        Assert.IsType<PageResult>(await page.OnPostAssignAsync(default));
        Assert.Contains("Someone changed this order", page.AssignError);
        Assert.NotNull(page.Order);
    }

    [Fact]
    public async Task Former_member_stays_selectable()
    {
        var page = Pages.With(new Details(Api("u-gone")) { Id = "o1" }, BusinessRole.Owner);
        await page.OnGetAsync(default);
        Assert.True(page.AssigneeIsFormer);
        Assert.Equal("Former team member", page.AssigneeName);
    }
}
