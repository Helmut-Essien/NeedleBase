using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Dashboard;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Identity;
using NeedleBase.Infrastructure.Persistence;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Api.Tests;

/// <summary>Slice 6: dashboard summary, staff names, and the board's order filters (sort=due, status=open, assignedTo).</summary>
public class DashboardApiTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly NeedleBaseApiFactory _factory;
    private readonly HttpClient _client;

    public DashboardApiTests(NeedleBaseApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Summary_counts_and_money_for_owner()
    {
        var (token, _, _) = await RegisterAsync("Sum");
        var today = BusinessTime.Today(DateTimeOffset.UtcNow);
        var overdue = await CreateOrderAsync(token, 300m, today.AddDays(-2));
        var soon = await CreateOrderAsync(token, 200m, today.AddDays(3), fitting: today.AddDays(1));
        var later = await CreateOrderAsync(token, 100m, today.AddDays(30));
        var cancelled = await CreateOrderAsync(token, 999m, today.AddDays(1));
        await SetStatusAsync(token, cancelled, OrderStatus.Cancelled);
        await SetStatusAsync(token, soon, OrderStatus.Sewing);
        Assert.Equal(HttpStatusCode.Created, await PayAsync(token, overdue, 100m));

        var s = await SummaryAsync(token);
        Assert.Equal(3, s.ActiveOrders);
        Assert.Equal(1, s.Overdue);
        Assert.Equal(1, s.DueNext7Days); // cancelled one excluded
        Assert.Equal(1, s.FittingsNext7Days);
        Assert.Equal(today, s.Today);
        Assert.Equal(OrderStatus.All, s.ByStatus.Select(b => b.Status));
        Assert.Equal(2, s.ByStatus.Single(b => b.Status == OrderStatus.New).Count);
        Assert.Equal(1, s.ByStatus.Single(b => b.Status == OrderStatus.Cancelled).Count);
        Assert.NotNull(s.Money);
        Assert.Equal(500m, s.Money!.Outstanding); // 200 + 200 + 100, cancelled excluded
        Assert.Equal(3, s.Money.OrdersWithBalance);
        Assert.Equal(100m, s.Money.ReceivedToday);
        Assert.Equal(100m, s.Money.ReceivedLast7Days);
        _ = later;
    }

    [Fact]
    public async Task Tailor_summary_has_no_money()
    {
        var (owner, businessId, _) = await RegisterAsync("Tl");
        await CreateOrderAsync(owner, 100m, null);
        var tailor = await TokenWithRoleAsync(owner, businessId, BusinessRole.Tailor);
        var s = await SummaryAsync(tailor);
        Assert.Null(s.Money);
        Assert.Equal(1, s.ActiveOrders);
        using var raw = await SendAsync(HttpMethod.Get, "/api/dashboard/summary", tailor);
        var json = await raw.Content.ReadAsStringAsync();
        Assert.DoesNotContain("outstanding", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Summary_is_tenant_scoped_and_requires_auth()
    {
        var (a, _, _) = await RegisterAsync("TA");
        var (b, _, _) = await RegisterAsync("TB");
        var order = await CreateOrderAsync(a, 400m, null);
        Assert.Equal(HttpStatusCode.Created, await PayAsync(a, order, 50m));
        var s = await SummaryAsync(b);
        Assert.Equal(0, s.ActiveOrders);
        Assert.Equal(0m, s.Money!.Outstanding);
        Assert.Equal(0m, s.Money.ReceivedToday);
        using var anon = await _client.GetAsync("/api/dashboard/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
    }

    [Fact]
    public async Task Staff_names_are_tenant_scoped_without_contact_details()
    {
        var (a, _, userA) = await RegisterAsync("SA");
        var (b, _, userB) = await RegisterAsync("SB");
        using var response = await SendAsync(HttpMethod.Get, "/api/staff/names", a);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("@", json);
        Assert.DoesNotContain("+233", json);
        var names = JsonSerializer.Deserialize<List<StaffNameDto>>(json, JsonOptions)!;
        var me = Assert.Single(names);
        Assert.Equal((userA, "Ama Owusu", BusinessRole.Owner), (me.UserId, me.Name, me.Role));
        Assert.DoesNotContain(names, n => n.UserId == userB);
        _ = b;
    }

    [Theory]
    [InlineData(null, null, "Team member")]
    [InlineData(" Kofi ", "", "Kofi")]
    [InlineData("Kofi", "Boateng", "Kofi Boateng")]
    public void Staff_name_compose(string? first, string? last, string expected) => Assert.Equal(expected, StaffNameDto.Compose(first, last));

    [Fact]
    public async Task Orders_sort_due_puts_nulls_last_and_open_excludes_terminal()
    {
        var (token, _, _) = await RegisterAsync("Due");
        var today = BusinessTime.Today(DateTimeOffset.UtcNow);
        var none = await CreateOrderAsync(token, 10m, null);
        var late = await CreateOrderAsync(token, 10m, today.AddDays(9));
        var early = await CreateOrderAsync(token, 10m, today.AddDays(2));
        var collected = await CreateOrderAsync(token, 10m, today.AddDays(1));
        foreach (var s in new[] { OrderStatus.Ready, OrderStatus.Collected })
        {
            await SetStatusAsync(token, collected, s);
        }

        var all = await ListAsync(token, "sort=due");
        Assert.Equal([collected, early, late, none], all.Data.Select(o => o.Id));
        var open = await ListAsync(token, "sort=due&status=open");
        Assert.Equal([early, late, none], open.Data.Select(o => o.Id));
        Assert.Equal(3, open.TotalCount);
    }

    [Fact]
    public async Task Orders_assigned_to_me_and_by_id()
    {
        var (owner, businessId, ownerId) = await RegisterAsync("Asg");
        var mine = await CreateOrderAsync(owner, 10m, null);
        await CreateOrderAsync(owner, 10m, null);
        using (var put = await SendAsync(HttpMethod.Put, $"/api/orders/{mine}", owner, new UpdateOrderRequest { AssignedToUserId = ownerId }))
        {
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        }

        var me = await ListAsync(owner, "assignedTo=me");
        var item = Assert.Single(me.Data);
        Assert.Equal((mine, ownerId), (item.Id, item.AssignedToUserId));
        Assert.Single((await ListAsync(owner, $"assignedTo={ownerId}")).Data);
        Assert.Equal(2, (await ListAsync(owner, "")).TotalCount);

        // Journey 3 (server side): a status change made by another device is what the web list shows.
        await SetStatusAsync(owner, mine, OrderStatus.Cutting);
        Assert.Equal(OrderStatus.Cutting, Assert.Single((await ListAsync(owner, "status=Cutting")).Data).Status);

        var (other, _, _) = await RegisterAsync("Asg2");
        Assert.Empty((await ListAsync(other, $"assignedTo={ownerId}")).Data); // tenant filter wins
        _ = businessId;
    }

    [Fact]
    public async Task Orders_unknown_sort_falls_back_and_paging_is_capped()
    {
        var (token, _, _) = await RegisterAsync("Pg");
        await CreateOrderAsync(token, 10m, null);
        var page = await ListAsync(token, "sort=bogus&pageSize=5000");
        Assert.Equal(PaginationDefaults.MaxPageSize, page.PageSize);
        Assert.Single(page.Data);
    }

    [Fact]
    public async Task Oversized_filter_values_are_validation_errors()
    {
        var (token, _, _) = await RegisterAsync("Big");
        using var response = await SendAsync(HttpMethod.Get, "/api/orders?assignedTo=" + new string('x', 65), token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Outstanding_ignores_overpaid_and_voided_and_keeps_collected_balance()
    {
        var (token, businessId, _) = await RegisterAsync("Math");
        var overpaid = await CreateOrderAsync(token, 100m, null);
        var voided = await CreateOrderAsync(token, 80m, null);
        var collected = await CreateOrderAsync(token, 60.55m, null);
        Assert.Equal(HttpStatusCode.Created, await PayAsync(token, overpaid, 100m));
        // A second offline device also took money (sync stores it): this order is overpaid by 50.
        await InsertPaymentAsync(businessId, overpaid, 50m);
        using (var pay = await SendAsync(HttpMethod.Post, $"/api/orders/{voided}/payments", token, new RecordPaymentRequest { Amount = 30m, Method = PaymentMethod.Cash }))
        {
            var paymentId = (await pay.Content.ReadFromJsonAsync<PaymentDto>(JsonOptions))!.Id;
            using var v = await SendAsync(HttpMethod.Post, $"/api/payments/{paymentId}/void", token, new VoidPaymentRequest { Reason = "typo" });
            Assert.Equal(HttpStatusCode.OK, v.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Created, await PayAsync(token, collected, 0.25m));
        foreach (var st in new[] { OrderStatus.Ready, OrderStatus.Collected })
        {
            await SetStatusAsync(token, collected, st);
        }

        var m = (await SummaryAsync(token)).Money!;
        Assert.Equal(80m + 60.30m, m.Outstanding); // overpaid order adds 0, never −50
        Assert.Equal(2, m.OrdersWithBalance);
        Assert.Equal(100m + 50m + 0.25m, m.ReceivedToday); // voided 30 excluded
        Assert.Equal(2, (await SummaryAsync(token)).ActiveOrders);
    }

    [Fact]
    public async Task Received_today_uses_accra_midnight_boundary()
    {
        var (token, businessId, _) = await RegisterAsync("Mid");
        var order = await CreateOrderAsync(token, 1000m, null);
        var todayStart = BusinessTime.StartOfDayUtc(BusinessTime.Today(DateTimeOffset.UtcNow));
        await InsertPaymentAsync(businessId, order, 1m, todayStart.AddTicks(-10));       // yesterday, last µs
        await InsertPaymentAsync(businessId, order, 2m, todayStart);                     // today, first instant
        await InsertPaymentAsync(businessId, order, 4m, todayStart.AddDays(-6));         // 7-day window start
        await InsertPaymentAsync(businessId, order, 8m, todayStart.AddDays(-6).AddTicks(-10)); // just outside
        var m = (await SummaryAsync(token)).Money!;
        Assert.Equal(2m, m.ReceivedToday);
        Assert.Equal(1m + 2m + 4m, m.ReceivedLast7Days);
    }

    [Fact]
    public async Task Concurrent_status_changes_with_same_base_only_one_wins()
    {
        for (var round = 0; round < 5; round++)
        {
            var (token, _, _) = await RegisterAsync("Race" + round);
            var order = await CreateOrderAsync(token, 10m, null);
            using var get = await SendAsync(HttpMethod.Get, $"/api/orders/{order}", token);
            var baseModified = (await get.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions))!.DateModifiedUtc;
            var tasks = new[] { OrderStatus.Cutting, OrderStatus.Sewing, OrderStatus.Cutting }.Select(async s =>
            {
                using var r = await SendAsync(HttpMethod.Patch, $"/api/orders/{order}/status", token,
                    new UpdateOrderStatusRequest { Status = s, BaseModifiedUtc = baseModified });
                return r.StatusCode;
            }).ToList();
            var codes = await Task.WhenAll(tasks);
            Assert.Equal(1, codes.Count(c => c == HttpStatusCode.OK));
            Assert.Equal(2, codes.Count(c => c == HttpStatusCode.Conflict));
        }
    }

    [Fact]
    public async Task Base_modified_round_trips_through_o_format()
    {
        var (token, _, _) = await RegisterAsync("Rt");
        var order = await CreateOrderAsync(token, 10m, null);
        using var get = await SendAsync(HttpMethod.Get, $"/api/orders/{order}", token);
        var seen = (await get.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions))!.DateModifiedUtc;
        // What the web form posts back: the "o" string, parsed with an offset (Accra is +00:00, but any offset works).
        var posted = DateTimeOffset.Parse(seen.ToOffset(TimeSpan.FromHours(3)).ToString("o"), System.Globalization.CultureInfo.InvariantCulture);
        using var patch = await SendAsync(HttpMethod.Patch, $"/api/orders/{order}/status", token, new UpdateOrderStatusRequest { Status = OrderStatus.Cutting, BaseModifiedUtc = posted });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
    }

    [Fact]
    public async Task Final_states_refuse_further_moves()
    {
        var (token, _, _) = await RegisterAsync("Fin");
        var order = await CreateOrderAsync(token, 10m, null);
        await SetStatusAsync(token, order, OrderStatus.Cancelled);
        using var r = await SendAsync(HttpMethod.Patch, $"/api/orders/{order}/status", token, new UpdateOrderStatusRequest { Status = OrderStatus.New });
        Assert.True(r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest, $"got {(int)r.StatusCode}");
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("code").GetString())); // typed code, no stack trace
        Assert.DoesNotContain("Exception", doc.RootElement.ToString());
    }

    [Theory]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("page=-5&pageSize=-5")]
    [InlineData("status=Nope&sort=%27%3B--")]
    [InlineData("search=%3Cscript%3E")]
    public async Task Tampered_list_queries_never_500(string query)
    {
        var (token, _, _) = await RegisterAsync("Tq");
        using var r = await SendAsync(HttpMethod.Get, "/api/orders?" + query, token);
        Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest, $"got {(int)r.StatusCode}");
    }

    private async Task InsertPaymentAsync(string businessId, string orderId, decimal amount, DateTimeOffset? at = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.CurrentBusinessId = businessId;
        var order = db.Orders.Single(o => o.Id == orderId);
        var now = DateTimeOffset.UtcNow;
        db.Payments.Add(Payment.Create(IdGenerator.NewId(), businessId, orderId, order.CustomerId, amount, PaymentMethod.Cash, null, at ?? now, now));
        await db.SaveChangesAsync();
    }

    private async Task<DashboardSummaryDto> SummaryAsync(string token)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/dashboard/summary", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DashboardSummaryDto>(JsonOptions))!;
    }

    private async Task<PaginatedResponse<OrderListItemDto>> ListAsync(string token, string query)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/orders?" + query, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PaginatedResponse<OrderListItemDto>>(JsonOptions))!;
    }

    private async Task SetStatusAsync(string token, string orderId, string status)
    {
        using var response = await SendAsync(HttpMethod.Patch, $"/api/orders/{orderId}/status", token, new UpdateOrderStatusRequest { Status = status });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpStatusCode> PayAsync(string token, string orderId, decimal amount)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/payments", token, new RecordPaymentRequest { Amount = amount, Method = PaymentMethod.Cash });
        return response.StatusCode;
    }

    private async Task<string> CreateOrderAsync(string token, decimal total, DateOnly? due, DateOnly? fitting = null)
    {
        using var customerResponse = await SendAsync(HttpMethod.Post, "/api/customers", token, new CreateCustomerRequest("Efua Mensah", "+233245550123", null, null));
        Assert.Equal(HttpStatusCode.Created, customerResponse.StatusCode);
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions))!;
        using var response = await SendAsync(HttpMethod.Post, "/api/orders", token, new CreateOrderRequest
        {
            CustomerId = customer.Id,
            TotalAmount = total,
            DueDate = due,
            FittingDate = fitting,
            Lines = [new CreateOrderLineRequest { GarmentTemplate = GarmentTemplate.Dress, Quantity = 1, UnitPrice = total, Unit = MeasurementUnit.Centimetres, MeasurementFields = new Dictionary<string, decimal?>() }]
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions))!.Id;
    }

    private async Task<string> TokenWithRoleAsync(string ownerToken, string businessId, string role)
    {
        using var me = await SendAsync(HttpMethod.Get, "/api/me", ownerToken);
        using var doc = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        var userId = doc.RootElement.GetProperty("userId").GetString()!;
        using var scope = _factory.Services.CreateScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId))!;
        return scope.ServiceProvider.GetRequiredService<ITokenService>()
            .CreateAccessToken(user.Id, user.Email!, user.UserName!, user.SecurityStamp ?? string.Empty, true, businessId, role);
    }

    private async Task<(string Token, string BusinessId, string UserId)> RegisterAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var response = await _client.PostAsJsonAsync("/api/auth/register-business",
            new RegisterBusinessRequest($"dash{label}{suffix}@ama-stitches.test", "Passw0rd!", "Ama", "Owusu", $"Dash {label} {suffix}", "+233200000000"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;
        using var me = await SendAsync(HttpMethod.Get, "/api/me", payload.AccessToken!);
        using var doc = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        return (payload.AccessToken!, payload.User!.BusinessId!, doc.RootElement.GetProperty("userId").GetString()!);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await _client.SendAsync(request);
    }

    private sealed record AuthPayload(string? AccessToken, string? RefreshToken, AuthUserDto? User);
}
