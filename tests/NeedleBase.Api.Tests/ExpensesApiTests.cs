using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Licensing;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Identity;
using NeedleBase.Infrastructure.Licensing;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Tests;

/// <summary>Plans step 7b-2: /api/expenses — record, list, get, void, plan and role gating, tenancy, read-only, audit.</summary>
public class ExpensesApiTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly NeedleBaseApiFactory _factory;
    private readonly HttpClient _client;

    public ExpensesApiTests(NeedleBaseApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static DateOnly Today => BusinessTime.Today(DateTimeOffset.UtcNow);

    private static string MonthKey(DateOnly d) => d.ToString("yyyy-MM");

    [Fact]
    public async Task Record_Is201_Replay200_DifferentContent409_AndGetReturnsIt()
    {
        var (token, _, userId) = await RegisterAsync("Rec");
        var id = IdGenerator.NewId();
        var request = new RecordExpenseRequest { Id = id, Amount = 120m, Category = ExpenseCategory.Materials, Note = " Makola lining " };

        using (var created = await SendAsync(HttpMethod.Post, "/api/expenses", token, request))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.EndsWith($"/api/expenses/{id}", created.Headers.Location!.ToString());
            var dto = (await created.Content.ReadFromJsonAsync<ExpenseDto>(JsonOptions))!;
            Assert.Equal(120m, dto.Amount);
            Assert.Equal(Today, dto.SpentOn);
            Assert.Equal("Makola lining", dto.Note);
            Assert.Equal(userId, dto.RecordedByUserId);
            Assert.Equal("GH₵ 120.00", dto.AmountDisplay);
            Assert.Equal("Fabric and materials", dto.CategoryDisplay);
        }

        using (var replay = await SendAsync(HttpMethod.Post, "/api/expenses", token, request))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        }

        using (var conflict = await SendAsync(HttpMethod.Post, "/api/expenses", token,
                   new RecordExpenseRequest { Id = id, Amount = 99m, Category = ExpenseCategory.Materials }))
        {
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("EXPENSE_CONFLICT", await CodeAsync(conflict));
        }

        var got = await GetAsync(token, id);
        Assert.Equal(120m, got.Amount);
        Assert.Equal(1, (await ListAsync(token, "")).Count);
    }

    [Fact]
    public async Task Record_RefusesBadEntries()
    {
        var (token, _, _) = await RegisterAsync("Bad");
        await AssertBadAsync(token, new RecordExpenseRequest { Amount = 10m, Category = ExpenseCategory.Other, SpentOn = Today.AddDays(1) }, "EXPENSE_INVALID");
        await AssertBadAsync(token, new RecordExpenseRequest { Amount = 10m, Category = ExpenseCategory.Other, SpentOn = new DateOnly(2019, 12, 31) }, "EXPENSE_INVALID");
        await AssertBadAsync(token, new RecordExpenseRequest { Amount = 10m, Category = "Fuel" }, "EXPENSE_INVALID");
        await AssertBadAsync(token, new RecordExpenseRequest { Amount = 10.005m, Category = ExpenseCategory.Other }, null);
        await AssertBadAsync(token, new RecordExpenseRequest { Amount = 0m, Category = ExpenseCategory.Other }, null);
        Assert.Equal(0, (await ListAsync(token, "")).Count);
    }

    [Fact]
    public async Task OrderLink_MustBeThisShopsOrder_AndBlocksOrderDelete()
    {
        var (a, _, _) = await RegisterAsync("OrdA");
        var (b, _, _) = await RegisterAsync("OrdB");
        var orderA = await CreateOrderAsync(a);
        var orderB = await CreateOrderAsync(b);

        await AssertBadAsync(a, new RecordExpenseRequest { Amount = 10m, Category = ExpenseCategory.Materials, OrderId = orderB }, "EXPENSE_ORDER_INVALID");
        await AssertBadAsync(a, new RecordExpenseRequest { Amount = 10m, Category = ExpenseCategory.Materials, OrderId = "01JNOSUCHORDER000000000001" }, "EXPENSE_ORDER_INVALID");

        var linked = await RecordAsync(a, new RecordExpenseRequest { Amount = 45m, Category = ExpenseCategory.Materials, OrderId = orderA });
        Assert.Equal(orderA, linked.OrderId);
        Assert.NotNull(linked.OrderDisplayRef);
        Assert.Equal("Efua Mensah", linked.CustomerName);

        await VoidOkAsync(a, linked.Id, "Wrong order");
        using var delete = await SendAsync(HttpMethod.Delete, $"/api/orders/{orderA}", a);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal("ORDER_HAS_EXPENSES", await CodeAsync(delete));

        // An order without expenses (or payments) still deletes.
        using var deleteB = await SendAsync(HttpMethod.Delete, $"/api/orders/{orderB}", b);
        Assert.True(deleteB.IsSuccessStatusCode);
    }

    [Fact]
    public async Task List_FiltersByMonthCategoryOrder_TotalsSkipVoided_Paginates()
    {
        var (token, businessId, _) = await RegisterAsync("List");
        var order = await CreateOrderAsync(token);
        var lastMonth = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        var day = new DateOnly(Today.Year, Today.Month, 1);

        await RecordAsync(token, new RecordExpenseRequest { Amount = 300m, Category = ExpenseCategory.RentAndBills, SpentOn = day });
        await RecordAsync(token, new RecordExpenseRequest { Amount = 100m, Category = ExpenseCategory.Materials, SpentOn = day, OrderId = order });
        await RecordAsync(token, new RecordExpenseRequest { Amount = 50.5m, Category = ExpenseCategory.Materials, SpentOn = Today });
        var voided = await RecordAsync(token, new RecordExpenseRequest { Amount = 999m, Category = ExpenseCategory.StaffPay, SpentOn = Today });
        await VoidOkAsync(token, voided.Id, "Entered twice");
        await RecordAsync(token, new RecordExpenseRequest { Amount = 20m, Category = ExpenseCategory.Materials, SpentOn = lastMonth, OrderId = order });

        var month = await ListAsync(token, "");
        Assert.Equal(MonthKey(Today), month.Month);
        Assert.Equal(450.5m, month.Total);
        Assert.Equal(3, month.Count);
        Assert.Equal(4, month.Items.TotalCount); // voided listed by default
        Assert.Equal([ExpenseCategory.RentAndBills, ExpenseCategory.Materials], month.ByCategory.Select(c => c.Category));
        Assert.Equal(150.5m, month.ByCategory.Single(c => c.Category == ExpenseCategory.Materials).Total);
        Assert.True(month.Items.Data.Zip(month.Items.Data.Skip(1)).All(p => p.First.SpentOn >= p.Second.SpentOn));

        Assert.Equal(3, (await ListAsync(token, "includeVoided=false")).Items.TotalCount);
        var materials = await ListAsync(token, $"category={ExpenseCategory.Materials}");
        Assert.Equal(150.5m, materials.Total);
        Assert.All(materials.Items.Data, e => Assert.Equal(ExpenseCategory.Materials, e.Category));

        var previous = await ListAsync(token, "month=" + MonthKey(lastMonth));
        Assert.Equal(20m, previous.Total);

        var forOrder = await ListAsync(token, "orderId=" + order);
        Assert.Null(forOrder.Month);
        Assert.Equal(120m, forOrder.Total); // every month
        Assert.Equal(20m, (await ListAsync(token, $"orderId={order}&month={MonthKey(lastMonth)}")).Total);

        var page1 = await ListAsync(token, "pageSize=2&page=1");
        var page2 = await ListAsync(token, "pageSize=2&page=2");
        Assert.Equal(2, page1.Items.Data.Count);
        Assert.True(page1.Items.HasNext);
        Assert.Equal(2, page2.Items.Data.Count);
        Assert.Empty(page1.Items.Data.Select(e => e.Id).Intersect(page2.Items.Data.Select(e => e.Id)));
        Assert.Equal(450.5m, page2.Total); // totals cover the whole filter, not the page

        foreach (var bad in new[] { "month=2019-12", "month=" + MonthKey(Today.AddMonths(1)), "month=nope", "category=Fuel" })
        {
            using var response = await SendAsync(HttpMethod.Get, "/api/expenses?" + bad, token);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("EXPENSE_FILTER_INVALID", await CodeAsync(response));
        }

        Assert.True(await CountEventsAsync(businessId, AuditActions.ExpensesViewed) == 1); // throttled: one per person per hour
    }

    [Fact]
    public async Task OtherShops_Expenses_ReadAsNotFound()
    {
        var (a, _, _) = await RegisterAsync("TenA");
        var (b, _, _) = await RegisterAsync("TenB");
        var mine = await RecordAsync(a, new RecordExpenseRequest { Amount = 70m, Category = ExpenseCategory.Transport });

        using (var get = await SendAsync(HttpMethod.Get, $"/api/expenses/{mine.Id}", b))
        {
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
            Assert.Equal("EXPENSE_NOT_FOUND", await CodeAsync(get));
        }

        using (var voidOther = await SendAsync(HttpMethod.Post, $"/api/expenses/{mine.Id}/void", b, new VoidExpenseRequest { Reason = "not mine" }))
        {
            Assert.Equal(HttpStatusCode.NotFound, voidOther.StatusCode);
        }

        // Same id from another shop can't take over the key.
        using (var steal = await SendAsync(HttpMethod.Post, "/api/expenses", b,
                   new RecordExpenseRequest { Id = mine.Id, Amount = 70m, Category = ExpenseCategory.Transport }))
        {
            Assert.Equal(HttpStatusCode.Conflict, steal.StatusCode);
        }

        Assert.Equal(0, (await ListAsync(b, "")).Count);
        Assert.False((await GetAsync(a, mine.Id)).IsVoided);
        using var anon = await _client.GetAsync("/api/expenses");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
    }

    [Fact]
    public async Task Void_IsIdempotent_FirstReasonWins_StaleIs409_ReasonRequired()
    {
        var (token, businessId, userId) = await RegisterAsync("Void");
        var expense = await RecordAsync(token, new RecordExpenseRequest { Amount = 80m, Category = ExpenseCategory.Equipment });

        using (var noReason = await SendAsync(HttpMethod.Post, $"/api/expenses/{expense.Id}/void", token, new VoidExpenseRequest { Reason = " x " }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
            Assert.Equal("EXPENSE_INVALID", await CodeAsync(noReason));
        }

        using (var stale = await SendAsync(HttpMethod.Post, $"/api/expenses/{expense.Id}/void", token,
                   new VoidExpenseRequest { Reason = "Wrong amount", BaseModifiedUtc = expense.DateModifiedUtc.AddSeconds(-5) }))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(StaleUpdate.Code, await CodeAsync(stale));
        }

        var first = await VoidOkAsync(token, expense.Id, "Wrong amount", expense.DateModifiedUtc);
        var second = await VoidOkAsync(token, expense.Id, "Another reason");
        Assert.True(first.IsVoided);
        Assert.Equal("Wrong amount", second.VoidReason);
        Assert.Equal(first.VoidedAtUtc, second.VoidedAtUtc);
        Assert.Equal(userId, second.VoidedByUserId);

        using (var missing = await SendAsync(HttpMethod.Post, "/api/expenses/01JNOSUCHEXPENSE0000000001/void", token, new VoidExpenseRequest { Reason = "gone" }))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        var events = await EventsAsync(businessId);
        var recorded = Assert.Single(events, e => e.Action == AuditActions.ExpenseRecorded);
        Assert.Equal(expense.Id, recorded.EntityId);
        Assert.Equal(userId, recorded.ActorUserId);
        Assert.Equal(BusinessRole.Owner, recorded.ActorRole);
        Assert.Contains("Wrong amount", Assert.Single(events, e => e.Action == AuditActions.ExpenseVoided).Summary);
    }

    [Fact]
    public async Task Tailor_IsForbidden_OnEveryRoute_ManagerAllowed()
    {
        var (owner, businessId, _) = await RegisterAsync("Role");
        var expense = await RecordAsync(owner, new RecordExpenseRequest { Amount = 10m, Category = ExpenseCategory.Other });
        var tailor = await TokenWithRoleAsync(owner, businessId, BusinessRole.Tailor);
        var calls = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, "/api/expenses", null),
            (HttpMethod.Get, $"/api/expenses/{expense.Id}", null),
            (HttpMethod.Post, "/api/expenses", new RecordExpenseRequest { Amount = 5m, Category = ExpenseCategory.Other }),
            (HttpMethod.Post, $"/api/expenses/{expense.Id}/void", new VoidExpenseRequest { Reason = "tailor try" })
        };
        foreach (var (method, url, body) in calls)
        {
            using var response = await SendAsync(method, url, tailor, body);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("EXPENSES_FORBIDDEN", await CodeAsync(response));
            Assert.DoesNotContain("\"amount\"", await response.Content.ReadAsStringAsync());
        }

        var manager = await TokenWithRoleAsync(owner, businessId, BusinessRole.Manager);
        Assert.Equal(1, (await ListAsync(manager, "")).Count);
        await RecordAsync(manager, new RecordExpenseRequest { Amount = 5m, Category = ExpenseCategory.Transport });
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
        var existing = await RecordAsync(token, new RecordExpenseRequest { Amount = 10m, Category = ExpenseCategory.Other }); // keyless: unlimited
        await SetPlanAsync(businessId, plan, complimentary, trial);

        using var list = await SendAsync(HttpMethod.Get, "/api/expenses", token);
        using var get = await SendAsync(HttpMethod.Get, $"/api/expenses/{existing.Id}", token);
        using var post = await SendAsync(HttpMethod.Post, "/api/expenses", token, new RecordExpenseRequest { Amount = 5m, Category = ExpenseCategory.Other });
        using var voided = await SendAsync(HttpMethod.Post, $"/api/expenses/{existing.Id}/void", token, new VoidExpenseRequest { Reason = "downgrade test" });
        foreach (var response in new[] { list, get, post, voided })
        {
            if (allowed)
            {
                Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage!.RequestUri} → {(int)response.StatusCode}");
            }
            else
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal("EXPENSES_NOT_ALLOWED", await CodeAsync(response));
            }
        }

        // A downgrade keeps the data: nothing was deleted.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.Expenses.IgnoreQueryFilters().AnyAsync(e => e.Id == existing.Id));
    }

    [Fact]
    public async Task ReadOnlyShop_CanList_ButNotRecordOrVoid()
    {
        var fake = new FakeLicensePlatform
        {
            Answer = _ => LicensePlatformOutcome.Judged(new LicenseCheck(true, "ok", "lic-1", PlanCodes.Workshop, false, false,
                DateTimeOffset.UtcNow.AddDays(200), DateTimeOffset.UtcNow.AddHours(12), DateTimeOffset.UtcNow.AddDays(3), "header.payload.sig"), true)
        };
        using var factory = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Platform:RequireLicenseKey"] = "true",
                ["Platform:LockGraceDays"] = "7"
            }));
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<ILicensePlatform>(fake);
                foreach (var d in s.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(LicenseRecheckWorker)).ToList())
                {
                    s.Remove(d);
                }
            });
        });
        var client = factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var register = await client.PostAsJsonAsync("/api/auth/register-business",
            new RegisterBusinessRequest($"expro{suffix}@ama-stitches.test", "Passw0rd!", "Ama", "Owusu", $"Expenses RO {suffix}", "+233200000000", "NB-KEY-EXP-RO"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var payload = (await register.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;
        var token = payload.AccessToken!;
        var businessId = payload.User!.BusinessId!;

        var expense = await SendWithAsync<ExpenseDto>(client, HttpMethod.Post, "/api/expenses", token,
            new RecordExpenseRequest { Amount = 30m, Category = ExpenseCategory.Transport }, HttpStatusCode.Created);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var business = await db.Businesses.SingleAsync(b => b.Id == businessId);
            var since = DateTimeOffset.UtcNow.AddDays(-30);
            business.RecordLicenseCheck(new LicenseCheck(false, "expired"), since);
            await db.SaveChangesAsync();
            await db.Businesses.Where(b => b.Id == businessId)
                .ExecuteUpdateAsync(u => u.SetProperty(b => b.LicenseInvalidSinceUtc, since).SetProperty(b => b.LicenseGraceUntilUtc, since));
            scope.ServiceProvider.GetRequiredService<LicenseService>().Invalidate(businessId);
        }

        using (var post = await SendWith(client, HttpMethod.Post, "/api/expenses", token, new RecordExpenseRequest { Amount = 5m, Category = ExpenseCategory.Other }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
            Assert.Equal(LicenseErrors.ReadOnly, await CodeAsync(post));
        }

        using (var voided = await SendWith(client, HttpMethod.Post, $"/api/expenses/{expense.Id}/void", token, new VoidExpenseRequest { Reason = "paused" }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, voided.StatusCode);
            Assert.Equal(LicenseErrors.ReadOnly, await CodeAsync(voided));
        }

        var list = await SendWithAsync<ExpenseListResponse>(client, HttpMethod.Get, "/api/expenses", token, null, HttpStatusCode.OK);
        Assert.Equal(30m, list.Total);
        await SendWithAsync<ExpenseDto>(client, HttpMethod.Get, $"/api/expenses/{expense.Id}", token, null, HttpStatusCode.OK);
    }

    [Fact]
    public async Task Export_IncludesLiveAndVoidedExpenses_EvenAfterDowngradeToStarter()
    {
        var (token, businessId, userId) = await RegisterAsync("Exp");
        var live = await RecordAsync(token, new RecordExpenseRequest
        {
            Id = IdGenerator.NewId(), Amount = 75.5m, Category = ExpenseCategory.Transport, SpentOn = Today, Note = "Trotro to Makola"
        });
        var gone = await RecordAsync(token, new RecordExpenseRequest
        {
            Id = IdGenerator.NewId(), Amount = 40m, Category = ExpenseCategory.Other, SpentOn = Today
        });
        await VoidOkAsync(token, gone.Id, "Wrong shop");

        async Task<List<JsonElement>> ExportedAsync()
        {
            using var export = await SendAsync(HttpMethod.Get, "/api/export", token);
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            using var doc = JsonDocument.Parse(await export.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("expenses").EnumerateArray().Select(e => e.Clone()).ToList();
        }

        var rows = await ExportedAsync();
        Assert.Equal(2, rows.Count);
        var liveRow = rows.Single(r => r.GetProperty("id").GetString() == live.Id);
        Assert.False(liveRow.GetProperty("isVoided").GetBoolean());
        Assert.Equal(75.5m, liveRow.GetProperty("amount").GetDecimal());
        Assert.Equal(ExpenseCategory.Transport, liveRow.GetProperty("category").GetString());
        Assert.Equal("Transport", liveRow.GetProperty("categoryLabel").GetString());
        Assert.Equal(Today.ToString("yyyy-MM-dd"), liveRow.GetProperty("spentOn").GetString());
        Assert.Equal("Trotro to Makola", liveRow.GetProperty("note").GetString());
        Assert.Equal(userId, liveRow.GetProperty("recordedByUserId").GetString());
        var voidedRow = rows.Single(r => r.GetProperty("id").GetString() == gone.Id);
        Assert.True(voidedRow.GetProperty("isVoided").GetBoolean());
        Assert.Equal("Wrong shop", voidedRow.GetProperty("voidReason").GetString());
        Assert.Equal(userId, voidedRow.GetProperty("voidedByUserId").GetString());

        // Downgrade: the expenses screens close, but the shop's data still exports.
        await SetPlanAsync(businessId, PlanCodes.Starter);
        using (var listed = await SendAsync(HttpMethod.Get, "/api/expenses", token))
        {
            Assert.Equal(HttpStatusCode.Forbidden, listed.StatusCode);
        }

        Assert.Equal(2, (await ExportedAsync()).Count);
    }

    private async Task AssertBadAsync(string token, RecordExpenseRequest request, string? code)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/expenses", token, request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        if (code is not null)
        {
            Assert.Equal(code, await CodeAsync(response));
        }
    }

    private async Task<ExpenseDto> RecordAsync(string token, RecordExpenseRequest request)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/expenses", token, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExpenseDto>(JsonOptions))!;
    }

    private async Task<ExpenseDto> VoidOkAsync(string token, string id, string reason, DateTimeOffset? baseModified = null)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/api/expenses/{id}/void", token, new VoidExpenseRequest { Reason = reason, BaseModifiedUtc = baseModified });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExpenseDto>(JsonOptions))!;
    }

    private async Task<ExpenseDto> GetAsync(string token, string id)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/expenses/{id}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExpenseDto>(JsonOptions))!;
    }

    private async Task<ExpenseListResponse> ListAsync(string token, string query)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/expenses?" + query, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExpenseListResponse>(JsonOptions))!;
    }

    private async Task<List<AuditEvent>> EventsAsync(string businessId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuditEvents.IgnoreQueryFilters().AsNoTracking().Where(e => e.BusinessId == businessId).ToListAsync();
    }

    private async Task<int> CountEventsAsync(string businessId, string action) =>
        (await EventsAsync(businessId)).Count(e => e.Action == action);

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
        business.AttachLicense("protected-test-key", "…TEST", new LicenseCheck(true, "ok", "lic-plan", planCode, trial, complimentary), DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<LicenseService>().Invalidate(businessId);
    }

    private async Task<string> CreateOrderAsync(string token)
    {
        using var customerResponse = await SendAsync(HttpMethod.Post, "/api/customers", token, new CreateCustomerRequest("Efua Mensah", "+233245550123", null, null));
        Assert.Equal(HttpStatusCode.Created, customerResponse.StatusCode);
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions))!;
        using var response = await SendAsync(HttpMethod.Post, "/api/orders", token, new CreateOrderRequest
        {
            CustomerId = customer.Id,
            TotalAmount = 800m,
            Lines = [new CreateOrderLineRequest { GarmentTemplate = GarmentTemplate.Dress, Quantity = 1, UnitPrice = 800m, Unit = MeasurementUnit.Centimetres, MeasurementFields = new Dictionary<string, decimal?>() }]
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
            new RegisterBusinessRequest($"exp{label.ToLowerInvariant()}{suffix}@ama-stitches.test", "Passw0rd!", "Ama", "Owusu", $"Expenses {label} {suffix}", "+233200000000"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;
        using var me = await SendAsync(HttpMethod.Get, "/api/me", payload.AccessToken!);
        using var doc = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        return (payload.AccessToken!, payload.User!.BusinessId!, doc.RootElement.GetProperty("userId").GetString()!);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null) =>
        SendWith(_client, method, url, token, body);

    private static async Task<HttpResponseMessage> SendWith(HttpClient client, HttpMethod method, string url, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        return await client.SendAsync(request);
    }

    private static async Task<T> SendWithAsync<T>(HttpClient client, HttpMethod method, string url, string token, object? body, HttpStatusCode expected)
    {
        using var response = await SendWith(client, method, url, token, body);
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private sealed record AuthPayload(string? AccessToken, string? RefreshToken, AuthUserDto? User);
}
