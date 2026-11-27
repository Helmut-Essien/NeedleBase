using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Application.Licensing;
using NeedleBase.Application.Sync;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Identity;
using NeedleBase.Infrastructure.Licensing;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Tests;

/// <summary>
/// Plans step 7b-2 chunk 5: expenses over /api/sync — push create/replay/void merge/mismatch/plan/role/delete/
/// detached order/lenient entry/read-only, and pull phase 5 (order, Tailor and Starter skip, cursor reset, old cursors).
/// </summary>
[Collection(SyncEpochCollection.Name)]
public class ExpenseSyncTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly NeedleBaseApiFactory _factory;
    private readonly HttpClient _client;

    public ExpenseSyncTests(NeedleBaseApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static DateOnly Today => BusinessTime.Today(DateTimeOffset.UtcNow);

    // ------------------------------------------------------------------ push

    [Fact]
    public async Task Push_Create_ThenReplay_IsAppliedOnce_WithServerCopy_AndRecorderIsCaller()
    {
        var (token, _, userId) = await RegisterAsync("PushNew");
        var dto = Expense(IdGenerator.NewId(), 120m, ExpenseCategory.Materials, Today.AddDays(-2), " Makola lining ");

        var created = await PushAsync(token, Op(dto));
        Assert.Equal(SyncOpStatus.Applied, created.Status);
        Assert.NotNull(created.Expense);
        Assert.Equal((120m, ExpenseCategory.Materials, Today.AddDays(-2), "Makola lining"),
            (created.Expense!.Amount, created.Expense.Category, created.Expense.SpentOn, created.Expense.Note));

        var replay = await PushAsync(token, Op(dto));
        Assert.Equal(SyncOpStatus.Applied, replay.Status);
        Assert.Equal(created.Expense.DateModifiedUtc, replay.Expense!.DateModifiedUtc);

        var list = await GetAsync<ExpenseListResponse>(token, $"/api/expenses?month={Today.AddDays(-2):yyyy-MM}");
        var row = Assert.Single(list.Items.Data, e => e.Id == dto.Id);
        Assert.Equal(userId, row.RecordedByUserId);
    }

    [Fact]
    public async Task Push_VoidMerges_FromEitherSide_AndFirstReasonWins()
    {
        var (token, _, userId) = await RegisterAsync("PushVoid");

        // Phone voids an expense the server has live.
        var a = Expense(IdGenerator.NewId(), 50m, ExpenseCategory.Transport, Today, null);
        Assert.Equal(SyncOpStatus.Applied, (await PushAsync(token, Op(a))).Status);
        var voided = await PushAsync(token, Op(a with { IsVoided = true, VoidReason = "Typed twice", VoidedAtUtc = DateTimeOffset.UtcNow }));
        Assert.Equal(SyncOpStatus.Applied, voided.Status);
        Assert.True(voided.Expense!.IsVoided);
        Assert.Equal("Typed twice", voided.Expense.VoidReason);

        // A later void with another reason keeps the first; an un-voided replay gets the voided server copy back.
        var again = await PushAsync(token, Op(a with { IsVoided = true, VoidReason = "Other reason" }));
        Assert.Equal(("Typed twice", true), (again.Expense!.VoidReason, again.Expense.IsVoided));
        var stale = await PushAsync(token, Op(a));
        Assert.Equal(SyncOpStatus.Applied, stale.Status);
        Assert.True(stale.Expense!.IsVoided);

        // Recorded and voided offline before the first push: lands voided (lenient: a blank reason is fine).
        var b = Expense(IdGenerator.NewId(), 70m, ExpenseCategory.Other, Today, null) with { IsVoided = true };
        var both = await PushAsync(token, Op(b));
        Assert.Equal(SyncOpStatus.Applied, both.Status);
        Assert.True(both.Expense!.IsVoided);

        var detail = await GetAsync<ExpenseDto>(token, $"/api/expenses/{a.Id}");
        Assert.Equal(userId, detail.VoidedByUserId);
    }

    [Fact]
    public async Task Push_DifferentContentSameId_IsMismatchConflict_WithServerCopy_OtherShopIdIsRejected()
    {
        var (token, _, _) = await RegisterAsync("PushMis");
        var dto = Expense(IdGenerator.NewId(), 120m, ExpenseCategory.Materials, Today, "lining");
        Assert.Equal(SyncOpStatus.Applied, (await PushAsync(token, Op(dto))).Status);

        var mismatch = await PushAsync(token, Op(dto with { Amount = 99m }));
        Assert.Equal(SyncOpStatus.Conflict, mismatch.Status);
        Assert.Equal(SyncErrorCodes.ExpenseMismatch, mismatch.Code);
        Assert.Equal(120m, mismatch.Expense!.Amount); // never overwritten

        var (otherToken, _, _) = await RegisterAsync("PushMisB");
        var foreign = await PushAsync(otherToken, Op(dto with { Amount = 5m }));
        Assert.Equal(SyncOpStatus.Failed, foreign.Status);
        Assert.Equal(SyncErrorCodes.Rejected, foreign.Code);
        Assert.Null(foreign.Expense);
    }

    [Fact]
    public async Task Push_PlanWithoutExpenses_Tailor_AndDeleteOp_AreFailed()
    {
        var (token, businessId, _) = await RegisterAsync("PushGate");
        var tailor = await TokenWithRoleAsync(token, businessId, BusinessRole.Tailor);

        var byTailor = await PushAsync(tailor, Op(Expense(IdGenerator.NewId(), 10m, ExpenseCategory.Other, Today, null)));
        Assert.Equal((SyncOpStatus.Failed, SyncErrorCodes.ExpensesForbidden), (byTailor.Status, byTailor.Code));

        var dto = Expense(IdGenerator.NewId(), 10m, ExpenseCategory.Other, Today, null);
        var delete = await PushAsync(token, new SyncPushOperation
        {
            OpId = IdGenerator.NewId(), EntityType = SyncEntityType.Expense, EntityId = dto.Id, Operation = SyncOperation.Delete
        });
        Assert.Equal((SyncOpStatus.Failed, SyncErrorCodes.PayloadInvalid), (delete.Status, delete.Code));

        await SetPlanAsync(businessId, PlanCodes.Starter);
        var onStarter = await PushAsync(token, Op(dto));
        Assert.Equal((SyncOpStatus.Failed, SyncErrorCodes.ExpensesNotAllowed), (onStarter.Status, onStarter.Code));

        await SetPlanAsync(businessId, PlanCodes.Workshop);
        Assert.Equal(SyncOpStatus.Applied, (await PushAsync(token, Op(dto))).Status);
    }

    [Fact]
    public async Task Push_UnknownOrder_KeepsExpense_DropsLink_AndReplayStillApplies()
    {
        var (token, _, _) = await RegisterAsync("PushDetach");
        var orderId = await CreateOrderAsync(token);
        var linked = Expense(IdGenerator.NewId(), 40m, ExpenseCategory.Materials, Today, "zips") with { OrderId = orderId };
        var kept = await PushAsync(token, Op(linked));
        Assert.Equal(orderId, kept.Expense!.OrderId);

        var gone = Expense(IdGenerator.NewId(), 60m, ExpenseCategory.Materials, Today, "buttons") with { OrderId = IdGenerator.NewId() };
        var detached = await PushAsync(token, Op(gone));
        Assert.Equal(SyncOpStatus.Applied, detached.Status);
        Assert.Null(detached.Expense!.OrderId);
        Assert.Equal("buttons " + NeedleBase.Domain.Entities.Expense.OrderRemovedMarker, detached.Expense.Note);

        // The phone hasn't adopted the server copy yet and sends the old link again: same expense, not a conflict.
        var replay = await PushAsync(token, Op(gone));
        Assert.Equal(SyncOpStatus.Applied, replay.Status);
        Assert.Null(replay.Expense!.OrderId);
    }

    [Fact]
    public async Task Push_IsLenient_UnknownCategoryToOther_FutureDateClamped_BadAmountFailed()
    {
        var (token, _, _) = await RegisterAsync("PushLenient");
        var odd = await PushAsync(token, Op(Expense(IdGenerator.NewId(), 15m, "Fuel", Today.AddDays(5), "Shell station")));
        Assert.Equal(SyncOpStatus.Applied, odd.Status);
        Assert.Equal((ExpenseCategory.Other, "Shell station [Fuel]", Today), (odd.Expense!.Category, odd.Expense.Note, odd.Expense.SpentOn));

        var bad = await PushAsync(token, Op(Expense(IdGenerator.NewId(), 0m, ExpenseCategory.Other, Today, null)));
        Assert.Equal((SyncOpStatus.Failed, SyncErrorCodes.PayloadInvalid), (bad.Status, bad.Code));
    }

    [Fact]
    public async Task Push_StillApplies_OnReadOnlyShop()
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
            new RegisterBusinessRequest($"exsro{suffix}@ama-stitches.test", "Passw0rd!", "Ama", "Owusu", $"Expense sync RO {suffix}", "+233200000000", "NB-KEY-EXS-RO"), JsonOptions);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        var payload = (await register.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;
        var token = payload.AccessToken!;
        var businessId = payload.User!.BusinessId!;

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

        // REST writes are blocked…
        using (var post = await SendWith(client, HttpMethod.Post, "/api/expenses", token, new RecordExpenseRequest { Amount = 5m, Category = ExpenseCategory.Other }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        }

        // …but offline work from the phone still lands.
        var pushed = await PushWith(client, token, Op(Expense(IdGenerator.NewId(), 25m, ExpenseCategory.Transport, Today, "trotro")));
        Assert.Equal(SyncOpStatus.Applied, pushed.Status);
        Assert.Equal(25m, pushed.Expense!.Amount);
    }

    // ------------------------------------------------------------------ pull

    [Fact]
    public async Task Pull_Expenses_ComeLast_AfterPayments_AndPageWithoutGaps()
    {
        var (token, _, _) = await RegisterAsync("PullOrder");
        var orderId = await CreateOrderAsync(token);
        using (var paid = await SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/payments", token,
                   new RecordPaymentRequest { Amount = 100m, Method = PaymentMethod.Cash }))
        {
            Assert.Equal(HttpStatusCode.Created, paid.StatusCode);
        }

        var ids = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await RecordAsync(token, 10m + i, ExpenseCategory.Materials, Today, orderId)).Id);
        }

        var pages = new List<SyncPullResponse>();
        string? cursor = null;
        SyncPullResponse page;
        do
        {
            page = await PullAsync(token, cursor, 2);
            pages.Add(page);
            cursor = page.Cursor;
        }
        while (page.HasMore);

        var firstWithExpenses = pages.FindIndex(p => (p.Expenses?.Count ?? 0) > 0);
        Assert.True(firstWithExpenses >= 0);
        var lastWithPayments = pages.FindLastIndex(p => (p.Payments?.Count ?? 0) > 0);
        var lastWithOrders = pages.FindLastIndex(p => p.Orders.Count > 0);
        Assert.True(lastWithPayments <= firstWithExpenses && lastWithOrders <= firstWithExpenses);
        var pulled = pages.SelectMany(p => p.Expenses ?? []).Select(e => e.Id).ToList();
        Assert.Equal(ids.Order(), pulled.Order()); // each once, none missing
        Assert.All(pages.SelectMany(p => p.Expenses ?? []), e => Assert.Equal(orderId, e.OrderId));

        // Incremental: a new expense after a completed sync arrives alone on the stored cursor.
        var later = await RecordAsync(token, 99m, ExpenseCategory.Transport, Today, null);
        var next = await PullAsync(token, cursor, 200);
        Assert.Equal([later.Id], (next.Expenses ?? []).Select(e => e.Id));
        Assert.Empty(next.Payments ?? []);
    }

    [Fact]
    public async Task Pull_Tailor_GetsNoExpenses_ManagerDoes()
    {
        var (token, businessId, _) = await RegisterAsync("PullTailor");
        var expense = await RecordAsync(token, 20m, ExpenseCategory.Other, Today, null);
        var tailor = await TokenWithRoleAsync(token, businessId, BusinessRole.Tailor);
        var manager = await TokenWithRoleAsync(token, businessId, BusinessRole.Manager);

        var (tailorExpenses, tailorCursor) = await PullAllAsync(tailor, null);
        Assert.Empty(tailorExpenses);
        Assert.True(SyncCursor.TryDecode(tailorCursor, out var decoded));
        Assert.Null(decoded.Expenses);

        // Same phone after a promotion: the tailor's cursor still delivers the older expense.
        var (managerExpenses, _) = await PullAllAsync(manager, tailorCursor);
        Assert.Contains(managerExpenses, e => e.Id == expense.Id);
    }

    [Fact]
    public async Task Pull_Starter_SkipsAndResetsCursor_ReUpgradePullsEverythingAgain()
    {
        var (token, businessId, _) = await RegisterAsync("PullPlan");
        var first = await RecordAsync(token, 20m, ExpenseCategory.Other, Today, null);
        var (pulled, cursor) = await PullAllAsync(token, null);
        Assert.Contains(pulled, e => e.Id == first.Id);
        Assert.True(SyncCursor.TryDecode(cursor, out var before));
        Assert.NotNull(before.Expenses);

        await SetPlanAsync(businessId, PlanCodes.Starter);
        var (onStarter, starterCursor) = await PullAllAsync(token, cursor);
        Assert.Empty(onStarter);
        Assert.True(SyncCursor.TryDecode(starterCursor, out var reset));
        Assert.Null(reset.Expenses);

        // Re-upgrade: the reset cursor pulls the older expense again, plus anything new.
        await SetPlanAsync(businessId, PlanCodes.Workshop);
        var second = await RecordAsync(token, 30m, ExpenseCategory.Transport, Today, null);
        var (again, _) = await PullAllAsync(token, starterCursor);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), again.Select(e => e.Id).Order());
    }

    [Fact]
    public async Task Pull_OldCursorWithoutExpensesKey_IsAccepted_AndReadsAllExpenses()
    {
        var (token, _, _) = await RegisterAsync("PullOld");
        var (_, cursor) = await PullAllAsync(token, null); // nothing yet: a phone synced before expenses existed

        // Rebuild the cursor the way an older server wrote it: no "z" key at all.
        Assert.True(SyncCursor.TryDecode(cursor, out var decoded));
        var json = JsonNode.Parse(JsonSerializer.Serialize(decoded))!.AsObject();
        json.Remove("z");
        var old = Convert.ToBase64String(Encoding.UTF8.GetBytes(json.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.DoesNotContain("\"z\"", Encoding.UTF8.GetString(Convert.FromBase64String(Pad(old.Replace('-', '+').Replace('_', '/')))));

        var expense = await RecordAsync(token, 45m, ExpenseCategory.RentAndBills, Today, null);
        var (pulled, _) = await PullAllAsync(token, old);
        Assert.Equal([expense.Id], pulled.Select(e => e.Id));
    }

    // ------------------------------------------------------------------ helpers

    private static string Pad(string base64) => base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');

    private static SyncExpenseDto Expense(string id, decimal amount, string category, DateOnly spentOn, string? note)
    {
        var now = DateTimeOffset.UtcNow;
        return new SyncExpenseDto(id, amount, category, spentOn, note, null, false, null, null, now, now);
    }

    private static SyncPushOperation Op(SyncExpenseDto expense) => new()
    {
        OpId = IdGenerator.NewId(),
        EntityType = SyncEntityType.Expense,
        EntityId = expense.Id,
        Expense = expense
    };

    private Task<SyncPushResult> PushAsync(string token, SyncPushOperation op) => PushWith(_client, token, op);

    private static async Task<SyncPushResult> PushWith(HttpClient client, string token, SyncPushOperation op)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await SendWith(client, HttpMethod.Post, "/api/sync/push", token, new SyncPushRequest { Operations = [op] });
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < 5 && await CodeAsync(response) == SyncErrorCodes.Maintenance)
            {
                await ApplicationDbContext.WaitForEpochBumpAsync().WaitAsync(TimeSpan.FromSeconds(60));
                await Task.Delay(50);
                continue;
            }

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<SyncPushResponse>(JsonOptions);
            return Assert.Single(body!.Results);
        }
    }

    private async Task<SyncPullResponse> PullAsync(string token, string? cursor, int limit)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await SendAsync(HttpMethod.Post, "/api/sync/pull", token, new SyncPullRequest { Cursor = cursor, Limit = limit });
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < 5 && await CodeAsync(response) == SyncErrorCodes.Maintenance)
            {
                await ApplicationDbContext.WaitForEpochBumpAsync().WaitAsync(TimeSpan.FromSeconds(60));
                await Task.Delay(50);
                continue;
            }

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<SyncPullResponse>(JsonOptions))!;
        }
    }

    private async Task<(List<SyncExpenseDto> Expenses, string Cursor)> PullAllAsync(string token, string? cursor)
    {
        var all = new List<SyncExpenseDto>();
        SyncPullResponse page;
        do
        {
            page = await PullAsync(token, cursor, 200);
            all.AddRange(page.Expenses ?? []);
            cursor = page.Cursor;
        }
        while (page.HasMore);

        return (all, cursor!);
    }

    private async Task<ExpenseDto> RecordAsync(string token, decimal amount, string category, DateOnly spentOn, string? orderId)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/expenses", token,
            new RecordExpenseRequest { Id = IdGenerator.NewId(), Amount = amount, Category = category, SpentOn = spentOn, OrderId = orderId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ExpenseDto>(JsonOptions))!;
    }

    private async Task<T> GetAsync<T>(string token, string url)
    {
        using var response = await SendAsync(HttpMethod.Get, url, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task SetPlanAsync(string businessId, string planCode)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var business = await db.Businesses.SingleAsync(b => b.Id == businessId);
        business.AttachLicense("protected-test-key", "…TEST", new LicenseCheck(true, "ok", "lic-plan", planCode), DateTimeOffset.UtcNow);
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
            new RegisterBusinessRequest($"exs{label.ToLowerInvariant()}{suffix}@ama-stitches.test", "Passw0rd!", "Ama", "Owusu", $"Expense sync {label} {suffix}", "+233200000000"), JsonOptions);
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

    private sealed record AuthPayload(string? AccessToken, string? RefreshToken, AuthUserDto? User);
}
