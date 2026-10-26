using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.WhatsApp;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;
using Xunit;
using Details = NeedleBase.Web.Pages.Orders.DetailsModel;
using Settings = NeedleBase.Web.Pages.Settings.WhatsAppModel;

namespace NeedleBase.Web.Tests;

public class WhatsAppPageTests
{
    [Fact]
    public async Task Open_records_opened_then_redirects_to_wa_me_with_edited_text()
    {
        var api = new FakeApi();
        var page = Pages.With(new Details(api), BusinessRole.Tailor);
        page.Id = "o1";
        page.WhatsAppText = "Hi Ama, see you Friday";
        var result = Assert.IsType<RedirectResult>(await page.OnPostWhatsAppAsync(default));
        Assert.Equal("https://wa.me/233245550123?text=Hi%20Ama%2C%20see%20you%20Friday", result.Url);
        Assert.Equal(("o1", "New"), Assert.Single(api.Opened));
    }

    [Fact]
    public async Task Open_with_bad_phone_explains_and_does_not_audit()
    {
        var api = new FakeApi { Order = id => ApiResult<OrderDetailDto>.Ok(Data.Detail(id) with { CustomerPhone = "12" }) };
        var page = Pages.With(new Details(api), BusinessRole.Owner);
        page.Id = "o1";
        Assert.IsType<Microsoft.AspNetCore.Mvc.RazorPages.PageResult>(await page.OnPostWhatsAppAsync(default));
        Assert.Null(page.WhatsAppDigits);
        Assert.NotNull(page.StatusError);
        Assert.Empty(api.Opened);
    }

    [Fact]
    public async Task Tailor_never_loads_consent_or_log_owner_does()
    {
        var api = new FakeApi();
        var tailor = Pages.With(new Details(api), BusinessRole.Tailor);
        tailor.Id = "o1";
        await tailor.OnGetAsync(default);
        Assert.DoesNotContain("wa-consent", api.Calls);
        Assert.DoesNotContain("wa-messages", api.Calls);
        Assert.Null(tailor.Consent);
        Assert.Contains("ORD-1", tailor.DefaultWhatsAppText);

        var owner = Pages.With(new Details(api), BusinessRole.Owner);
        owner.Id = "o1";
        await owner.OnGetAsync(default);
        Assert.NotNull(owner.Consent);
        Assert.NotNull(owner.WhatsAppLog);
    }

    [Fact]
    public async Task Tailor_cannot_change_consent()
    {
        var api = new FakeApi();
        var page = Pages.With(new Details(api), BusinessRole.Tailor);
        page.Id = "o1";
        page.OptIn = true;
        await page.OnPostConsentAsync(default);
        Assert.Empty(api.ConsentChanges);
        Assert.NotNull(page.StatusError);
    }

    [Fact]
    public async Task Settings_page_tailor_blocked_and_unconfigured_shown()
    {
        var api = new FakeApi();
        var tailor = Pages.With(new Settings(api), BusinessRole.Tailor);
        await tailor.OnGetAsync(default);
        Assert.NotNull(tailor.PageError);
        Assert.Empty(api.Calls);

        var owner = Pages.With(new Settings(api), BusinessRole.Owner);
        await owner.OnGetAsync(default);
        Assert.False(owner.Settings!.Configured);
        Assert.True(owner.Loaded);
    }

    [Fact]
    public async Task Settings_save_error_is_shown()
    {
        var api = new FakeApi { UpdateWhatsApp = _ => Data.Problem<WhatsAppSettingsDto>(System.Net.HttpStatusCode.Conflict, WhatsAppErrorCodes.NotConfigured, "Not set up.") };
        var owner = Pages.With(new Settings(api), BusinessRole.Owner);
        owner.Enabled = true;
        await owner.OnPostAsync(default);
        Assert.Equal("Not set up.", owner.ActionError);
    }
}
