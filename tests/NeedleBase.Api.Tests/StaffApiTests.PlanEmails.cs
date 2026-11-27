using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Email;
using NeedleBase.Application.Licensing;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Email;
using NeedleBase.Infrastructure.Licensing;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Tests;

public sealed partial class StaffApiTests
{
    [Fact]
    public async Task AssignmentEmail_sent_on_create_when_plan_allows()
    {
        var mail = UseEmail();
        var owner = await RegisterAsync("asgn"); // keyless → unlimited entitlements
        var (tailorId, tailor) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);

        await CreateOrderAsync(owner.Token, tailorId);

        var message = await mail.WaitForAsync(tailor.User!.Email, "order_assigned");
        Assert.Contains("assigned to you", message.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Efua Mensah", message.TextBody);
    }

    [Fact]
    public async Task AssignmentEmail_not_sent_on_starter()
    {
        var mail = UseEmail();
        var owner = await RegisterAsync("asgn0");
        await SetPlanAsync(owner.BusinessId, PlanCodes.Starter);
        var (tailorId, tailor) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);

        await CreateOrderAsync(owner.Token, tailorId);
        await Task.Delay(300);

        Assert.DoesNotContain(mail.Sent, m => m.ToAddress == tailor.User!.Email && m.Kind == "order_assigned");
    }

    [Fact]
    public async Task ReadOnly_does_not_advance_monthly_watermark()
    {
        var mail = UseEmail();
        var owner = await RegisterAsync("ro1");
        await SetPlanAsync(owner.BusinessId, PlanCodes.Workshop);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var business = await db.Businesses.SingleAsync(b => b.Id == owner.BusinessId);
            var since = DateTimeOffset.UtcNow.AddDays(-30);
            business.RecordLicenseCheck(new LicenseCheck(false, "expired"), since);
            await db.SaveChangesAsync();
            await db.Businesses.Where(b => b.Id == owner.BusinessId)
                .ExecuteUpdateAsync(u => u.SetProperty(b => b.LicenseInvalidSinceUtc, since)
                    .SetProperty(b => b.LicenseIsValid, false)
                    .SetProperty(b => b.LicenseGraceUntilUtc, since));
        }

        var today = BusinessTime.Today(DateTimeOffset.UtcNow);
        var noon = new DateTimeOffset(today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
        var month = PlanEmailSchedule.MonthStart(today);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var other in await db.Businesses.Where(b => b.Id != owner.BusinessId).ToListAsync())
            {
                other.MarkMonthlySummarySent(month);
                other.MarkWeeklySummarySent(PlanEmailSchedule.WeekStartMonday(today));
            }

            await db.SaveChangesAsync();

            var emailOptions = new EmailOptions
            {
                Host = "smtp.example.test",
                Username = "shop@example.test",
                Password = "x",
                AppBaseUrl = "https://app.example.test"
            };
            var service = new PlanEmailService(
                db,
                new DirectCapturingSender(mail, emailOptions),
                new StaticOptionsMonitor<EmailOptions>(emailOptions),
                new StaticOptionsMonitor<PlatformOptions>(new PlatformOptions { LockGraceDays = 7 }),
                new FixedClock(noon),
                NullLogger<PlanEmailService>.Instance);

            await service.ProcessDueAsync();
            var still = await db.Businesses.AsNoTracking().SingleAsync(b => b.Id == owner.BusinessId);
            Assert.Null(still.MonthlySummarySentFor);
            Assert.DoesNotContain(mail.Sent, m => m.Kind == "monthly_summary" && m.ToAddress == owner.Email);
        }

        // Renew → same period should send.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var business = await db.Businesses.SingleAsync(b => b.Id == owner.BusinessId);
            business.RecordLicenseCheck(
                new LicenseCheck(true, "ok", "lic-ro", PlanCodes.Workshop), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();

            var emailOptions = new EmailOptions
            {
                Host = "smtp.example.test",
                Username = "shop@example.test",
                Password = "x",
                AppBaseUrl = "https://app.example.test"
            };
            var service = new PlanEmailService(
                db,
                new DirectCapturingSender(mail, emailOptions),
                new StaticOptionsMonitor<EmailOptions>(emailOptions),
                new StaticOptionsMonitor<PlatformOptions>(new PlatformOptions { LockGraceDays = 7 }),
                new FixedClock(noon),
                NullLogger<PlanEmailService>.Instance);
            await service.ProcessDueAsync();
        }

        var monthly = await mail.WaitForAsync(owner.Email, "monthly_summary");
        Assert.Contains("Open orders", monthly.TextBody);
    }

    [Fact]
    public async Task Reassign_clears_due_watermark_so_new_assignee_gets_due_email()
    {
        var mail = UseEmail();
        var owner = await RegisterAsync("due2");
        await SetPlanAsync(owner.BusinessId, PlanCodes.Workshop);
        var (fromId, _) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        var (toId, toTokens) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);

        var today = BusinessTime.Today(DateTimeOffset.UtcNow);
        string orderId;
        using (var customerResponse = await SendAsync(HttpMethod.Post, "/api/customers", owner.Token,
                   new CreateCustomerRequest("Ama Due", "+233245550111", null, null)))
        {
            var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions))!;
            using var orderResponse = await SendAsync(HttpMethod.Post, "/api/orders", owner.Token, new CreateOrderRequest
            {
                CustomerId = customer.Id,
                DueDate = today,
                TotalAmount = 50,
                AssignedToUserId = fromId,
                Lines =
                [
                    new CreateOrderLineRequest
                    {
                        GarmentTemplate = GarmentTemplate.Shirt, Quantity = 1, UnitPrice = 50,
                        Unit = MeasurementUnit.Centimetres, MeasurementFields = new Dictionary<string, decimal?>()
                    }
                ]
            });
            Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
            orderId = (await orderResponse.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions))!.Id;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = await db.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == orderId);
            order.MarkDueEmailSent(today); // simulate worker already claimed for the previous assignee
            await db.SaveChangesAsync();
        }

        while (mail.Sent.TryDequeue(out _))
        {
        }

        using var moved = await SendAsync(HttpMethod.Post, $"/api/staff/{fromId}/reassign-orders", owner.Token,
            new ReassignOrdersRequest { ToUserId = toId });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var after = await db2.Orders.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Null(after.DueEmailSentForDate);

        var noon = new DateTimeOffset(today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
        var emailOptions = new EmailOptions
        {
            Host = "smtp.example.test",
            Username = "shop@example.test",
            Password = "x",
            AppBaseUrl = "https://app.example.test"
        };
        var service = new PlanEmailService(
            db2,
            new DirectCapturingSender(mail, emailOptions),
            new StaticOptionsMonitor<EmailOptions>(emailOptions),
            new StaticOptionsMonitor<PlatformOptions>(new PlatformOptions()),
            new FixedClock(noon),
            NullLogger<PlanEmailService>.Instance);

        // Isolate this order from other shops' due rows.
        await db2.Orders.IgnoreQueryFilters()
            .Where(o => o.Id != orderId && o.DueDate == today && o.DueEmailSentForDate != today)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.DueEmailSentForDate, today));

        await service.ProcessDueAsync();
        var due = await mail.WaitForAsync(toTokens.User!.Email, "order_due");
        Assert.Contains("due today", due.TextBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reassign_sends_one_bulk_digest_not_one_per_order()
    {
        var mail = UseEmail();
        var owner = await RegisterAsync("bulk");
        var (fromId, _) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        var (toId, toTokens) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);

        await CreateOrderAsync(owner.Token, fromId);
        await CreateOrderAsync(owner.Token, fromId);
        await CreateOrderAsync(owner.Token, fromId);
        while (mail.Sent.TryDequeue(out _))
        {
        }

        using var moved = await SendAsync(HttpMethod.Post, $"/api/staff/{fromId}/reassign-orders", owner.Token,
            new ReassignOrdersRequest { ToUserId = toId });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var digest = await mail.WaitForAsync(toTokens.User!.Email, "order_assigned_bulk");
        Assert.Contains("3 orders", digest.TextBody);
        Assert.Equal(1, mail.Sent.Count(m => m.Kind is "order_assigned" or "order_assigned_bulk"));
    }

    [Fact]
    public async Task DueMonthlyAndWeeklyEmails_queued_for_workshop()
    {
        var mail = UseEmail();
        var owner = await RegisterAsync("due1");
        await SetPlanAsync(owner.BusinessId, PlanCodes.Workshop);
        var (tailorId, tailor) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);

        var today = BusinessTime.Today(DateTimeOffset.UtcNow);
        using (var customerResponse = await SendAsync(HttpMethod.Post, "/api/customers", owner.Token,
                   new CreateCustomerRequest("Yaw Due", "+233245550999", null, null)))
        {
            Assert.Equal(HttpStatusCode.Created, customerResponse.StatusCode);
            var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions))!;
            using var orderResponse = await SendAsync(HttpMethod.Post, "/api/orders", owner.Token, new CreateOrderRequest
            {
                CustomerId = customer.Id,
                DueDate = today,
                TotalAmount = 100,
                AssignedToUserId = tailorId,
                Lines =
                [
                    new CreateOrderLineRequest
                    {
                        GarmentTemplate = GarmentTemplate.Shirt, Quantity = 1, UnitPrice = 100,
                        Unit = MeasurementUnit.Centimetres, MeasurementFields = new Dictionary<string, decimal?>()
                    }
                ]
            });
            Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        }

        while (mail.Sent.TryDequeue(out _))
        {
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var month = PlanEmailSchedule.MonthStart(today);
        var week = PlanEmailSchedule.WeekStartMonday(today);
        // Shared test DB: mark every other shop/order so this pass only targets our shop.
        foreach (var other in await db.Businesses.Where(b => b.Id != owner.BusinessId).ToListAsync())
        {
            other.MarkMonthlySummarySent(month);
            other.MarkWeeklySummarySent(week);
        }

        await db.Orders.IgnoreQueryFilters()
            .Where(o => o.BusinessId != owner.BusinessId && o.DueDate == today && o.DueEmailSentForDate != today)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.DueEmailSentForDate, today));
        await db.SaveChangesAsync();

        var noon = new DateTimeOffset(today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
        var emailOptions = new EmailOptions
        {
            Host = "smtp.example.test",
            Username = "shop@example.test",
            Password = "x",
            AppBaseUrl = "https://app.example.test"
        };
        var service = new PlanEmailService(
            db,
            new DirectCapturingSender(mail, emailOptions),
            new StaticOptionsMonitor<EmailOptions>(emailOptions),
            new StaticOptionsMonitor<PlatformOptions>(new PlatformOptions()),
            new FixedClock(noon),
            NullLogger<PlanEmailService>.Instance);

        var queued = await service.ProcessDueAsync();
        Assert.True(queued >= 3, $"expected due + monthly + weekly, got {queued}; sent={mail.Sent.Count}");

        var due = await mail.WaitForAsync(tailor.User!.Email, "order_due");
        Assert.Contains("due today", due.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Open orders", (await mail.WaitForAsync(owner.Email, "monthly_summary")).TextBody);
        Assert.Contains("Balances owed", (await mail.WaitForAsync(owner.Email, "weekly_summary")).TextBody);

        Assert.Equal(0, await service.ProcessDueAsync());
    }

    [Fact]
    public async Task MonthlySummary_adds_last_month_profit_for_workshop_not_starter()
    {
        var mail = UseEmail();
        var workshop = await RegisterAsync("prof1");
        var starter = await RegisterAsync("prof2");
        var today = BusinessTime.Today(DateTimeOffset.UtcNow);
        var month = PlanEmailSchedule.MonthStart(today);
        var lastMonth = month.AddMonths(-1);

        // Record while keyless (expenses allowed), then set the plans.
        foreach (var (amount, voided) in new[] { (120m, false), (80m, false), (999m, true) })
        {
            var id = IdGenerator.NewId();
            using var created = await SendAsync(HttpMethod.Post, "/api/expenses", workshop.Token,
                new NeedleBase.Application.Contracts.Expenses.RecordExpenseRequest
                {
                    Id = id, Amount = amount, Category = ExpenseCategory.Materials, SpentOn = lastMonth.AddDays(3)
                });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            if (voided)
            {
                using var v = await SendAsync(HttpMethod.Post, $"/api/expenses/{id}/void", workshop.Token,
                    new NeedleBase.Application.Contracts.Expenses.VoidExpenseRequest { Reason = "typo" });
                Assert.Equal(HttpStatusCode.OK, v.StatusCode);
            }
        }

        await SetPlanAsync(workshop.BusinessId, PlanCodes.Workshop);
        await SetPlanAsync(starter.BusinessId, PlanCodes.Starter);
        while (mail.Sent.TryDequeue(out _))
        {
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ours = new[] { workshop.BusinessId, starter.BusinessId };
        foreach (var other in await db.Businesses.Where(b => !ours.Contains(b.Id)).ToListAsync())
        {
            other.MarkMonthlySummarySent(month);
            other.MarkWeeklySummarySent(PlanEmailSchedule.WeekStartMonday(today));
        }

        foreach (var shop in await db.Businesses.Where(b => ours.Contains(b.Id)).ToListAsync())
        {
            shop.MarkWeeklySummarySent(PlanEmailSchedule.WeekStartMonday(today));
        }

        await db.SaveChangesAsync();

        var noon = new DateTimeOffset(today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
        var emailOptions = new EmailOptions
        {
            Host = "smtp.example.test",
            Username = "shop@example.test",
            Password = "x",
            AppBaseUrl = "https://app.example.test"
        };
        var service = new PlanEmailService(
            db,
            new DirectCapturingSender(mail, emailOptions),
            new StaticOptionsMonitor<EmailOptions>(emailOptions),
            new StaticOptionsMonitor<PlatformOptions>(new PlatformOptions()),
            new FixedClock(noon),
            NullLogger<PlanEmailService>.Instance);
        await service.ProcessDueAsync();

        var label = lastMonth.ToString("MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        var withProfit = (await mail.WaitForAsync(workshop.Email, "monthly_summary")).TextBody;
        Assert.Contains($"Last month ({label})", withProfit);
        Assert.Contains("Money in: GH₵ 0.00\nSpent: GH₵ 200.00\nLoss: GH₵ 200.00", withProfit); // voided 999 ignored
        Assert.Contains($"https://app.example.test/Reports?month={lastMonth:yyyy-MM}", withProfit);

        var plain = (await mail.WaitForAsync(starter.Email, "monthly_summary")).TextBody;
        Assert.Contains("Open orders", plain);
        Assert.DoesNotContain("Spent:", plain);
        Assert.DoesNotContain("Last month", plain);
    }

    private sealed class DirectCapturingSender(CapturingTransport transport, EmailOptions options) : IEmailSender
    {
        public bool IsConfigured => options.IsConfigured;

        public bool TryQueue(EmailMessage message)
        {
            transport.Sent.Enqueue(message);
            return true;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T> where T : class
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
