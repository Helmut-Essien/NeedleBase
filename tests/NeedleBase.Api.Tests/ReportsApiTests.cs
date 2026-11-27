using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Reports;
using NeedleBase.Application.Licensing;
using NeedleBase.Application.Reports;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Identity;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Tests;

/// <summary>Plan step 7b: GET /api/reports/monthly — numbers, month handling, plan and role gating, tenancy.</summary>
public class ReportsApiTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly NeedleBaseApiFactory _factory;
    private readonly HttpClient _client;

    public ReportsApiTests(NeedleBaseApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static DateOnly ThisMonth => ReportRules.FirstOfMonth(BusinessTime.Today(DateTimeOffset.UtcNow));

    [Fact]
    public async Task Keyless_owner_gets_this_month_with_money_orders_and_still_owed()
    {
        var (token, businessId, _) = await RegisterAsync("Num");
        var partPaid = await CreateOrderAsync(token, 300m);
        var paid = await CreateOrderAsync(token, 200m);
        var cancelled = await CreateOrderAsync(token, 999m);
        await SetStatusAsync(token, cancelled, OrderStatus.Cancelled);
        Assert.Equal(HttpStatusCode.Created, await PayAsync(token, partPaid, 100m, PaymentMethod.MoMo));
        Assert.Equal(HttpStatusCode.Created, await PayAsync(token, paid, 200m, PaymentMethod.Cash));
        var voidedId = await PayReturningIdAsync(token, partPaid, 50m);
        using (var v = await SendAsync(HttpMethod.Post, $"/api/payments/{voidedId}/void", token, new VoidPaymentRequest { Reason = "typo" }))
        {
            Assert.Equal(HttpStatusCode.OK, v.StatusCode);
        }

        // Last month: an older order and payment (moved back in time directly in the database).
        var lastMonth = ThisMonth.AddMonths(-1);
        var old = await CreateOrderAsync(token, 80m);
        await MoveOrderAsync(old, BusinessTime.StartOfDayUtc(lastMonth).AddHours(9));
        await InsertPaymentAsync(businessId, old, 30m, BusinessTime.StartOfDayUtc(lastMonth).AddHours(10));

        var r = await ReportAsync(token, null);
        Assert.Equal(ReportRules.Key(ThisMonth), r.Month);
        Assert.True(r.IsCurrentMonth);
        Assert.Null(r.NextMonth);
        Assert.Equal(300m, r.MoneyReceived); // voided 50 excluded
        Assert.Equal(2, r.PaymentCount);
        Assert.Equal(30m, r.PreviousMoneyReceived);
        Assert.Equal(2, r.OrdersTaken);
        Assert.Equal(500m, r.OrdersTakenValue);
        Assert.Equal(200m, r.StillOwed);
        Assert.Equal(1, r.OrdersStillOwing);
        Assert.Equal(1, r.OrdersCancelled);
        Assert.Equal([(PaymentMethod.Cash, 200m), (PaymentMethod.MoMo, 100m)], r.ByMethod.Select(m => (m.Method, m.Amount)));
        Assert.Equal(ReportRules.TrendMonths, r.Trend.Count);
        var prev = r.Trend[^2];
        Assert.Equal((ReportRules.Key(lastMonth), 30m, 1, 80m), (prev.Month, prev.MoneyReceived, prev.OrdersTaken, prev.OrdersTakenValue));

        var earlier = await ReportAsync(token, ReportRules.Key(lastMonth));
        Assert.Equal(30m, earlier.MoneyReceived);
        Assert.Equal(1, earlier.OrdersTaken);
        Assert.Equal(50m, earlier.StillOwed);
        Assert.Equal(ReportRules.Key(ThisMonth), earlier.NextMonth);
        Assert.False(earlier.IsCurrentMonth);
    }

    [Fact]
    public async Task Expenses_give_spent_profit_or_loss_by_category_and_trend()
    {
        var (token, _, _) = await RegisterAsync("Prof");
        var order = await CreateOrderAsync(token, 500m);
        Assert.Equal(HttpStatusCode.Created, await PayAsync(token, order, 300m, PaymentMethod.Cash));

        var lastMonth = ThisMonth.AddMonths(-1);
        await RecordExpenseAsync(token, 120m, ExpenseCategory.Materials, ThisMonth);
        await RecordExpenseAsync(token, 500m, ExpenseCategory.StaffPay, ThisMonth);
        var voided = await RecordExpenseAsync(token, 999m, ExpenseCategory.RentAndBills, ThisMonth);
        using (var v = await SendAsync(HttpMethod.Post, $"/api/expenses/{voided}/void", token, new VoidExpenseRequest { Reason = "typo" }))
        {
            Assert.Equal(HttpStatusCode.OK, v.StatusCode);
        }

        await RecordExpenseAsync(token, 40m, ExpenseCategory.Transport, lastMonth.AddDays(27));

        using (var raw = await SendAsync(HttpMethod.Get, "/api/reports/monthly", token))
        {
            var json = await raw.Content.ReadAsStringAsync();
            Assert.Contains("\"spent\":", json); // additive fields are on the wire for web and phone
            Assert.Contains("\"isLoss\":true", json);
            Assert.Contains("\"spentByCategory\":[", json);
        }

        var r = await ReportAsync(token, null);
        Assert.Equal((300m, 620m, 2, -320m), (r.MoneyReceived, r.Spent, r.ExpenseCount, r.Profit));
        Assert.True(r.IsLoss);
        Assert.Equal(("Loss", "GH₵ 320.00"), (r.ProfitLabel, r.ProfitDisplay));
        Assert.Equal(40m, r.PreviousSpent);
        Assert.Equal(ReportRules.ProfitNote, r.ProfitNote);
        Assert.Equal([(ExpenseCategory.StaffPay, 500m), (ExpenseCategory.Materials, 120m)],
            r.SpentByCategory.Select(c => (c.Category, c.Total)));
        Assert.Equal((40m, -40m), (r.Trend[^2].Spent, r.Trend[^2].Profit));

        var earlier = await ReportAsync(token, ReportRules.Key(lastMonth));
        Assert.Equal((40m, 1, -40m), (earlier.Spent, earlier.ExpenseCount, earlier.Profit));
        Assert.Equal([ExpenseCategory.Transport], earlier.SpentByCategory.Select(c => c.Category));
    }

    [Fact]
    public async Task Empty_month_and_first_month_are_zero()
    {
        var (token, _, _) = await RegisterAsync("Emp");
        var r = await ReportAsync(token, "2020-01");
        Assert.True(r.IsEmpty);
        Assert.Null(r.PreviousMonth);
        Assert.All(r.Trend, t => Assert.Equal(0m, t.MoneyReceived));
    }

    [Theory]
    [InlineData("2099-01")]
    [InlineData("2019-12")]
    [InlineData("2026-13")]
    [InlineData("nope")]
    [InlineData("2026-1")]
    public async Task Bad_or_future_month_is_400(string month)
    {
        var (token, _, _) = await RegisterAsync("Bad");
        using var response = await SendAsync(HttpMethod.Get, "/api/reports/monthly?month=" + Uri.EscapeDataString(month), token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ReportRules.MonthInvalidCode, await CodeAsync(response));
    }

    [Fact]
    public async Task Next_month_is_400()
    {
        var (token, _, _) = await RegisterAsync("Nxt");
        using var response = await SendAsync(HttpMethod.Get, "/api/reports/monthly?month=" + ReportRules.Key(ThisMonth.AddMonths(1)), token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(PlanCodes.Workshop, false, false, true)]
    [InlineData(PlanCodes.Production, false, false, true)]
    [InlineData(PlanCodes.Starter, false, true, true)]   // complimentary → unlimited
    [InlineData(PlanCodes.Starter, false, false, false)]
    [InlineData(PlanCodes.Workshop, true, false, false)] // trial → Starter
    [InlineData("pro", false, false, false)]             // unknown → Starter (fail closed)
    [InlineData("", false, false, false)]                // blank → Starter
    public async Task Plan_gating(string plan, bool trial, bool complimentary, bool allowed)
    {
        var (token, businessId, _) = await RegisterAsync("Pl");
        await SetPlanAsync(businessId, plan, complimentary, trial);
        using var response = await SendAsync(HttpMethod.Get, "/api/reports/monthly", token);
        if (allowed)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(ReportRules.NotAllowedCode, await CodeAsync(response));
            Assert.DoesNotContain("moneyReceived", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        var status = await StatusAsync(token);
        Assert.Equal(allowed, status.CanReports);
    }

    [Fact]
    public async Task Tailor_is_forbidden_manager_allowed()
    {
        var (owner, businessId, _) = await RegisterAsync("Rl");
        await SetPlanAsync(businessId, PlanCodes.Workshop);
        var tailor = await TokenWithRoleAsync(owner, businessId, BusinessRole.Tailor);
        using (var t = await SendAsync(HttpMethod.Get, "/api/reports/monthly", tailor))
        {
            Assert.Equal(HttpStatusCode.Forbidden, t.StatusCode);
            Assert.Equal(ReportRules.ForbiddenCode, await CodeAsync(t));
        }

        var manager = await TokenWithRoleAsync(owner, businessId, BusinessRole.Manager);
        using var m = await SendAsync(HttpMethod.Get, "/api/reports/monthly", manager);
        Assert.Equal(HttpStatusCode.OK, m.StatusCode);
    }

    [Fact]
    public async Task Report_is_tenant_scoped_and_requires_auth()
    {
        var (a, _, _) = await RegisterAsync("TA");
        var (b, _, _) = await RegisterAsync("TB");
        var order = await CreateOrderAsync(a, 400m);
        Assert.Equal(HttpStatusCode.Created, await PayAsync(a, order, 50m, PaymentMethod.Cash));
        Assert.Equal(50m, (await ReportAsync(a, null)).MoneyReceived);
        var other = await ReportAsync(b, null);
        Assert.True(other.IsEmpty);
        Assert.Equal(0m, other.StillOwed);
        using var anon = await _client.GetAsync("/api/reports/monthly");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
    }

    private async Task<string> RecordExpenseAsync(string token, decimal amount, string category, DateOnly spentOn)
    {
        var id = IdGenerator.NewId();
        using var response = await SendAsync(HttpMethod.Post, "/api/expenses", token,
            new RecordExpenseRequest { Id = id, Amount = amount, Category = category, SpentOn = spentOn });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return id;
    }

    private async Task<MonthlyReportDto> ReportAsync(string token, string? month)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/reports/monthly" + (month is null ? "" : "?month=" + month), token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MonthlyReportDto>(JsonOptions))!;
    }

    private async Task<SubscriptionStatusDto> StatusAsync(string token)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/subscription", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubscriptionStatusDto>(JsonOptions))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task SetPlanAsync(string businessId, string planCode, bool complimentary = false, bool trial = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var business = await db.Businesses.SingleAsync(b => b.Id == businessId);
        business.AttachLicense(
            "protected-test-key",
            "…TEST",
            new LicenseCheck(true, "ok", "lic-plan", planCode, trial, complimentary),
            DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<NeedleBase.Infrastructure.Licensing.LicenseService>().Invalidate(businessId);
    }

    private async Task MoveOrderAsync(string orderId, DateTimeOffset createdAtUtc)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Orders\" SET \"CreatedAtUtc\" = {createdAtUtc} WHERE \"Id\" = {orderId}");
    }

    private async Task InsertPaymentAsync(string businessId, string orderId, decimal amount, DateTimeOffset at)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.CurrentBusinessId = businessId;
        var order = db.Orders.Single(o => o.Id == orderId);
        db.Payments.Add(Payment.Create(IdGenerator.NewId(), businessId, orderId, order.CustomerId, amount, PaymentMethod.Cash, null, at, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task SetStatusAsync(string token, string orderId, string status)
    {
        using var response = await SendAsync(HttpMethod.Patch, $"/api/orders/{orderId}/status", token, new UpdateOrderStatusRequest { Status = status });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpStatusCode> PayAsync(string token, string orderId, decimal amount, string method)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/payments", token, new RecordPaymentRequest { Amount = amount, Method = method });
        return response.StatusCode;
    }

    private async Task<string> PayReturningIdAsync(string token, string orderId, decimal amount)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/payments", token, new RecordPaymentRequest { Amount = amount, Method = PaymentMethod.Cash });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PaymentDto>(JsonOptions))!.Id;
    }

    private async Task<string> CreateOrderAsync(string token, decimal total)
    {
        using var customerResponse = await SendAsync(HttpMethod.Post, "/api/customers", token, new CreateCustomerRequest("Efua Mensah", "+233245550123", null, null));
        Assert.Equal(HttpStatusCode.Created, customerResponse.StatusCode);
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions))!;
        using var response = await SendAsync(HttpMethod.Post, "/api/orders", token, new CreateOrderRequest
        {
            CustomerId = customer.Id,
            TotalAmount = total,
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
            new RegisterBusinessRequest($"rep{label}{suffix}@ama-stitches.test", "Passw0rd!", "Ama", "Owusu", $"Reports {label} {suffix}", "+233200000000"), JsonOptions);
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
