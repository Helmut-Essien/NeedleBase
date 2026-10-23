using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Tests;

/// <summary>Renders the real Razor pages (layout, partials, role-aware nav) against an in-memory Api. No network, no data.</summary>
public class RenderTests : IClassFixture<RenderTests.WebFactory>
{
    public sealed class WebFactory : WebApplicationFactory<Program>
    {
        public FakeApi Api { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Api:BaseUrl", "http://127.0.0.1:1/");
            builder.ConfigureLogging(l => l.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<INeedleBaseApi>();
                services.AddSingleton<INeedleBaseApi>(Api);
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(o =>
                {
                    o.DefaultScheme = TestAuth.SchemeName;
                    o.DefaultAuthenticateScheme = TestAuth.SchemeName;
                    o.DefaultChallengeScheme = TestAuth.SchemeName;
                });
            });
        }
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
        : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "u1"), new Claim(ClaimTypes.Name, "ama@test.invalid"),
                new Claim(ClaimTypes.Role, role.ToString()), new Claim("BusinessName", "Test Stitches"),
                new Claim("FirstName", "Ama"), new Claim("Surname", "Owusu"), new Claim("AccessToken", "x")
            };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }

    private readonly WebFactory _factory;

    public RenderTests(WebFactory factory)
    {
        _factory = factory;
        factory.Api.Orders = q => ApiResult<PaginatedResponse<OrderListItemDto>>.Ok(Data.Page<OrderListItemDto>(
            q.Status is null or "open" or "Sewing" ? [Data.Item("o1", "Sewing", "u2")] : []));
        factory.Api.Staff = () => ApiResult<IReadOnlyList<StaffNameDto>>.Ok([new StaffNameDto("u2", "Kofi Boateng", "Tailor")]);
        factory.Api.Payments = () => ApiResult<PaginatedResponse<PaymentListItemDto>>.Ok(Data.Page<PaymentListItemDto>(
            [new PaymentListItemDto("p1", "o1", "ORD-1", "c1", "Ama Mensah", 1234.5m, PaymentMethod.MoMo, Data.Modified, false, Data.Modified)]));
    }

    private async Task<(HttpStatusCode Status, string Html)> GetAsync(string url, string role)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var response = await client.GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Production")]
    [InlineData("/Orders")]
    [InlineData("/Orders?status=open&search=ama&p=2")]
    [InlineData("/Orders/Details/o1")]
    [InlineData("/Customers")]
    [InlineData("/Payments")]
    public async Task Owner_pages_render_with_shell(string url)
    {
        var (status, html) = await GetAsync(url, BusinessRole.Owner);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("nb-sidebar", html);
        Assert.Contains("Test Stitches", html);
        Assert.Contains(">Payments</a>", html);
        Assert.Contains("Privacy Policy", html);
        Assert.DoesNotContain("notification", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">Staff<", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Production")]
    [InlineData("/Orders/Details/o1")]
    public async Task Tailor_sees_no_payments_anywhere(string url)
    {
        var (status, html) = await GetAsync(url, BusinessRole.Tailor);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain(">Payments</a>", html);
        Assert.DoesNotContain("Received", html);
        Assert.DoesNotContain("Balance", html);
        Assert.DoesNotContain("Outstanding", html);
        Assert.DoesNotContain("Left to collect", html);
        Assert.DoesNotContain("Recent payments", html);
        Assert.DoesNotContain("nb-hero", html);
    }

    [Fact]
    public async Task Board_shows_assignee_and_columns()
    {
        var (_, html) = await GetAsync("/Production?show=all", BusinessRole.Owner);
        Assert.Contains("Kofi Boateng", html);
        foreach (var col in new[] { "New", "Cutting", "Sewing", "Fitting", "Ready" })
        {
            Assert.Contains(col, html);
        }
    }

    [Fact]
    public async Task Payments_render_in_cedis()
    {
        var (_, html) = await GetAsync("/Payments", BusinessRole.Manager);
        Assert.Contains(Money.Format(1234.5m), WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task Signed_in_pages_are_not_cached_but_css_is()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", BusinessRole.Owner);
        using var page = await client.GetAsync("/Orders");
        Assert.Contains("no-store", page.Headers.CacheControl!.ToString());
        using var css = await client.GetAsync("/css/site.css");
        Assert.DoesNotContain("no-store", css.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Expired_antiforgery_post_shows_friendly_page()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", BusinessRole.Owner);
        using var r = await client.PostAsync("/Orders/Details/o1?handler=Status",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["NewStatus"] = "Ready" }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("That form has expired", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Board_see_all_keeps_mine_filter()
    {
        _factory.Api.Orders = q => ApiResult<PaginatedResponse<OrderListItemDto>>.Ok(Data.Page<OrderListItemDto>([Data.Item("o1", q.Status ?? "New")], total: 40, size: 30));
        var (_, html) = await GetAsync("/Production?show=me", BusinessRole.Tailor);
        Assert.Contains("assigned=me", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task Customer_names_are_html_encoded()
    {
        _factory.Api.Orders = _ => ApiResult<PaginatedResponse<OrderListItemDto>>.Ok(Data.Page<OrderListItemDto>(
            [Data.Item("o1") with { CustomerName = "<script>alert(1)</script>" }]));
        var (_, html) = await GetAsync("/Orders", BusinessRole.Owner);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task Details_has_antiforgery_and_base_modified()
    {
        var (_, html) = await GetAsync("/Orders/Details/o1", BusinessRole.Tailor);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains("name=\"BaseModified\"", html);
    }

    private static NeedleBase.Application.Contracts.Audit.AuditEventDto Activity(string action, string summary) =>
        new("e1", Data.Modified, "u2", "Kofi Boateng", "Manager", action, "order", "o1", "o1", "c1", summary,
            [new NeedleBase.Application.Contracts.Audit.AuditChangeDto("Status", "New", "Cutting")], "web", null, null, null);

    [Fact]
    public async Task Activity_page_lists_entries_with_filters_and_nav_for_managers()
    {
        _factory.Api.Activity = _ => ApiResult<PaginatedResponse<NeedleBase.Application.Contracts.Audit.AuditEventDto>>.Ok(
            Data.Page([Activity("order.status_changed", "Order #ORD-1: New → Cutting")]));
        var (status, html) = await GetAsync("/Activity?kind=order.", BusinessRole.Manager);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Order #ORD-1: New", html);
        Assert.Contains("Kofi Boateng (Manager)", html);
        Assert.Contains("20 Oct 2026, 09:30", html); // Accra time
        Assert.Contains("<summary>Details</summary>", html);
        Assert.Contains("href=\"/Activity\"", html); // nav link
        Assert.Contains("<label for=\"who\"", html);
        Assert.Contains(">status</th>", html); // plain field labels
    }

    [Fact]
    public async Task Activity_keeps_a_former_members_filter_visible()
    {
        _factory.Api.Activity = _ => ApiResult<PaginatedResponse<NeedleBase.Application.Contracts.Audit.AuditEventDto>>.Ok(
            Data.Page<NeedleBase.Application.Contracts.Audit.AuditEventDto>([]));
        var (_, html) = await GetAsync("/Activity?who=gone-user", BusinessRole.Owner);
        Assert.Contains("value=\"gone-user\" selected", html);
        Assert.Contains("Nothing matches these filters.", html);
    }

    [Fact]
    public async Task Activity_is_hidden_from_tailors()
    {
        var (_, html) = await GetAsync("/Activity", BusinessRole.Tailor);
        Assert.Contains("only shown to the owner and managers", html);
        Assert.DoesNotContain(">Activity</a>", html); // no nav link
    }

    [Fact]
    public async Task Access_denied_has_no_pointless_try_again()
    {
        var (_, html) = await GetAsync("/Payments", BusinessRole.Tailor);
        Assert.Contains("only shown to the owner and managers", html);
        Assert.DoesNotContain("Try again", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Orders")]
    [InlineData("/Orders/Details/o1")]
    public async Task Shell_has_accessible_drawer_and_no_inline_styles_or_bootstrap_js(string url)
    {
        var (_, html) = await GetAsync(url, BusinessRole.Owner);
        Assert.Contains("id=\"nb-drawer\"", html);
        Assert.Contains("aria-controls=\"nb-drawer\"", html);
        Assert.Contains("aria-expanded=\"false\"", html);
        Assert.Contains("href=\"#nb-drawer\"", html); // works without JS (:target)
        Assert.DoesNotContain("style=\"", html);       // CSP: style-src 'self'
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("bootstrap.bundle", html);
        Assert.DoesNotContain("data-bs-", html);
        Assert.Contains("rel=\"apple-touch-icon\"", html);
        Assert.DoesNotContain("needlebase-logo.png", html);
    }
}
