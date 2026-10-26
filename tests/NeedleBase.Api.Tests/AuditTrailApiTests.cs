using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Audit;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Audit;
using NeedleBase.Infrastructure.Identity;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Tests;

/// <summary>Audit trail: every write path records who/what/when, masked; tenancy, roles, throttling, integrity, purge.</summary>
[Collection(SyncEpochCollection.Name)]
public class AuditTrailApiTests : IClassFixture<NeedleBaseApiFactory>
{
    private const string Phone = "+233245550123";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly NeedleBaseApiFactory _factory;
    private readonly HttpClient _client;

    public AuditTrailApiTests(NeedleBaseApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RestWrites_EachRecordAnEvent_WithActorSourceAndOrderLink()
    {
        var (token, businessId, _) = await RegisterAsync("Rest");
        var (customerId, orderId) = await CreateOrderAsync(token, 800m);
        await SendOk(HttpMethod.Patch, $"/api/orders/{orderId}/status", token, new UpdateOrderStatusRequest { Status = OrderStatus.Cutting });
        var payment = await RecordAsync(token, orderId, 200m);
        await SendOk(HttpMethod.Post, $"/api/payments/{payment.Id}/void", token, new VoidPaymentRequest { Reason = "Entered twice by mistake" });

        var events = await EventsAsync(businessId);
        Assert.Contains(events, e => e.Action == AuditActions.CustomerCreated && e.EntityId == customerId && e.CustomerId == customerId);
        Assert.Contains(events, e => e.Action == AuditActions.OrderCreated && e.EntityId == orderId && e.CustomerId == customerId);
        Assert.Contains(events, e => e.Action == AuditActions.OrderStatusChanged && e.OrderId == orderId && e.ChangesJson!.Contains("Cutting"));
        Assert.Contains(events, e => e.Action == AuditActions.PaymentRecorded && e.EntityId == payment.Id && e.OrderId == orderId);
        var voided = Assert.Single(events, e => e.Action == AuditActions.PaymentVoided);
        Assert.Contains("Entered twice", voided.Summary);
        Assert.All(events.Where(e => !e.Action.StartsWith("auth.")), e =>
        {
            Assert.Equal(AuditSource.Api, e.Source);
            Assert.NotNull(e.ActorUserId);
            Assert.Equal(BusinessRole.Owner, e.ActorRole);
        });

        // The order's history through the API, newest first.
        var page = await ListAsync(token, $"orderId={orderId}");
        Assert.True(page.TotalCount >= 4);
        Assert.Equal(AuditActions.PaymentVoided, page.Data[0].Action);
        Assert.Equal("Ama Owusu", page.Data[0].ActorName);
        Assert.All(page.Data, e => Assert.True(e.OrderId == orderId || e.EntityId == orderId));
    }

    [Fact]
    public async Task WebHeader_LabelsSourceWeb_AndCustomerUpdateDiffsAreMasked()
    {
        var (token, businessId, _) = await RegisterAsync("Web");
        var (customerId, _) = await CreateOrderAsync(token, 100m);
        using (var request = Authorized(HttpMethod.Put, $"/api/customers/{customerId}", token,
                   new UpdateCustomerRequest("Efua Mensah-Boateng", "+233201112222", "Prefers a loose fit at the hips", null)))
        {
            request.Headers.Add("X-NeedleBase-Client", "web");
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var updated = Assert.Single(await EventsAsync(businessId), e => e.Action == AuditActions.CustomerUpdated);
        Assert.Equal(AuditSource.Web, updated.Source);
        Assert.Contains("Mensah-Boateng", updated.ChangesJson);
        Assert.DoesNotContain("201112222", updated.ChangesJson); // phones keep only the last 4 digits
        Assert.Contains("2222", updated.ChangesJson);
        Assert.DoesNotContain("loose fit", updated.ChangesJson + updated.Summary); // notes are never copied
        Assert.DoesNotContain(Phone, string.Join("|", (await EventsAsync(businessId)).Select(e => e.Summary + e.ChangesJson)));
    }

    [Fact]
    public async Task Roles_TailorForbidden_ManagerSeesDataButNoSecurityOrNetwork_OwnerSeesAll()
    {
        var (ownerToken, businessId, _) = await RegisterAsync("Roles");
        await CreateOrderAsync(ownerToken, 300m);
        var tailor = await TokenWithRoleAsync(ownerToken, businessId, BusinessRole.Tailor);
        var manager = await TokenWithRoleAsync(ownerToken, businessId, BusinessRole.Manager);

        using (var response = await SendAsync(HttpMethod.Get, "/api/audit", tailor))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        var asManager = await ListAsync(manager, "pageSize=100");
        Assert.DoesNotContain(asManager.Data, e => e.Action.StartsWith("auth."));
        Assert.Contains(asManager.Data, e => e.Action == AuditActions.OrderCreated);
        Assert.All(asManager.Data, e => { Assert.Null(e.IpAddress); Assert.Null(e.UserAgent); });
        Assert.Empty((await ListAsync(manager, "action=auth.")).Data);

        var asOwner = await ListAsync(ownerToken, "pageSize=100");
        Assert.Contains(asOwner.Data, e => e.Action == AuditActions.Registered);
        Assert.Contains(asOwner.Data, e => e.Action == AuditActions.LoginSucceeded || e.Action == AuditActions.Registered);
    }

    [Fact]
    public async Task Tenancy_AnotherBusinessSeesNothingOfOurs()
    {
        var (tokenA, _, _) = await RegisterAsync("TenA");
        var (_, orderA) = await CreateOrderAsync(tokenA, 100m);
        var (tokenB, _, _) = await RegisterAsync("TenB");

        Assert.Equal(0, (await ListAsync(tokenB, $"orderId={orderA}")).TotalCount);
        Assert.DoesNotContain((await ListAsync(tokenB, "pageSize=100")).Data, e => e.EntityId == orderA || e.OrderId == orderA);
    }

    [Fact]
    public async Task Filters_ActionPrefix_Dates_Search_AndValidation()
    {
        var (token, _, _) = await RegisterAsync("Filt");
        await CreateOrderAsync(token, 100m);
        var today = BusinessTime.Today(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd");

        Assert.All((await ListAsync(token, "action=order.")).Data, e => Assert.StartsWith("order.", e.Action));
        Assert.NotEmpty((await ListAsync(token, $"from={today}&to={today}")).Data);
        Assert.Empty((await ListAsync(token, "from=2021-01-01&to=2021-01-02")).Data);
        Assert.NotEmpty((await ListAsync(token, "search=Efua")).Data);
        Assert.Empty((await ListAsync(token, "search=%25_%25")).Data); // wildcards are literal

        using var bad = await SendAsync(HttpMethod.Get, $"/api/audit?from={today}&to=2021-01-01", token);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task SyncPush_RecordsSourceSyncAndDevice_PlusOneSessionRow()
    {
        var (token, businessId, _) = await RegisterAsync("Sync");
        var device = "device-" + Guid.NewGuid().ToString("N")[..10];
        var now = DateTimeOffset.UtcNow;
        var customer = new SyncCustomerDto(IdGenerator.NewId(), "Kojo Badu", Phone, null, null, now, now);
        var created0 = (await PushAsync(token, device, [CustomerOp(customer)]))[0].Customer!;
        await PushAsync(token, device, [CustomerOp(created0 with { Name = "Kojo Badu Jnr" }, created0.DateModifiedUtc)]);

        var events = await EventsAsync(businessId);
        var created = Assert.Single(events, e => e.Action == AuditActions.CustomerCreated && e.EntityId == customer.Id);
        Assert.Equal(AuditSource.Sync, created.Source);
        Assert.Equal(device, created.DeviceId);
        Assert.Contains(events, e => e.Action == AuditActions.CustomerUpdated && e.EntityId == customer.Id && e.Source == AuditSource.Sync);
        Assert.Single(events, e => e.Action == AuditActions.SyncPush && e.DeviceId == device); // second quiet push folded

        // Pulls write one row per session per hour, and never carry audit rows.
        var pull = await PullAsync(token, device);
        await PullAsync(token, device);
        Assert.Single(await EventsAsync(businessId), e => e.Action == AuditActions.SyncPull && e.DeviceId == device);
        Assert.DoesNotContain("auditEvent", JsonSerializer.Serialize(pull, JsonOptions), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SyncDelete_ThenRestore_AreBothRecorded()
    {
        var (token, businessId, _) = await RegisterAsync("Rest2");
        var now = DateTimeOffset.UtcNow;
        var customer = new SyncCustomerDto(IdGenerator.NewId(), "Abena Owusu", Phone, null, null, now, now);
        var server = (await PushAsync(token, "d1", [CustomerOp(customer)]))[0].Customer!;
        await PushAsync(token, "d1", [new SyncPushOperation
        {
            OpId = IdGenerator.NewId(), EntityType = SyncEntityType.Customer, EntityId = customer.Id, Operation = SyncOperation.Delete, BaseModifiedUtc = server.DateModifiedUtc
        }]);
        await PushAsync(token, "d1", [CustomerOp(server)]); // "Restore" on the phone re-sends it as a create

        var events = await EventsAsync(businessId);
        Assert.Contains(events, e => e.Action == AuditActions.CustomerDeleted && e.EntityId == customer.Id);
        Assert.Contains(events, e => e.Action == AuditActions.CustomerRestored && e.EntityId == customer.Id);
    }

    [Fact]
    public async Task BulkPush_200Customers_RecordsEachQuickly()
    {
        var (token, businessId, _) = await RegisterAsync("Bulk");
        var now = DateTimeOffset.UtcNow;
        var ops = Enumerable.Range(0, 200)
            .Select(i => CustomerOp(new SyncCustomerDto(IdGenerator.NewId(), $"Customer {i}", Phone, null, null, now, now)))
            .ToList();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await PushAsync(token, "bulk", ops);
        watch.Stop();

        Assert.Equal(200, (await EventsAsync(businessId)).Count(e => e.Action == AuditActions.CustomerCreated));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"200-item push took {watch.Elapsed}");
    }

    [Fact]
    public async Task FailedSave_LeavesNoAuditRow()
    {
        var (token, businessId, _) = await RegisterAsync("Roll");
        var (customerId, _) = await CreateOrderAsync(token, 100m);
        var before = (await EventsAsync(businessId)).Count;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.CurrentBusinessId = businessId;
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                var customer = await db.Customers.SingleAsync(c => c.Id == customerId);
                customer.Update("Rolled Back", Phone, null, null, DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
                Assert.True(await db.AuditEvents.IgnoreQueryFilters().AnyAsync(e => e.EntityId == customerId && e.Action == AuditActions.CustomerUpdated));
                await tx.RollbackAsync(); // e.g. a later step in the same unit of work failed
            });
        }

        Assert.Equal(before, (await EventsAsync(businessId)).Count);
    }

    [Fact]
    public async Task Rows_AreAppendOnly_ButThePurgeRemovesExpiredOnes()
    {
        var (_, businessId, _) = await RegisterAsync("Guard");
        var oldId = IdGenerator.NewId();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AuditEnabled = false;
            db.AuditEvents.Add(AuditEvent.Create(oldId, businessId, AuditActions.CustomerCreated, "Old entry", DateTimeOffset.UtcNow.AddDays(-3 * 365)));
            await db.SaveChangesAsync();

            await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AuditEvents\" SET \"Summary\" = 'x' WHERE \"Id\" = {oldId}"));
            await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"AuditEvents\" WHERE \"Id\" = {oldId}"));
        }

        var purged = await AuditRetentionService.PurgeAsync(_factory.Services.GetRequiredService<IServiceScopeFactory>(),
            DateTimeOffset.UtcNow.AddDays(-AuditRetentionService.DefaultRetentionDays), CancellationToken.None);
        Assert.True(purged >= 1);
        var events = await EventsAsync(businessId);
        Assert.DoesNotContain(events, e => e.Id == oldId);
        Assert.Contains(events, e => e.Action == AuditActions.Registered); // recent rows stay
    }

    [Fact]
    public async Task Logins_FailuresAreThrottled_UnknownEmailIsMasked_LoginAndLogoutRecorded()
    {
        var (_, businessId, email) = await RegisterAsync("Login");
        for (var i = 0; i < 3; i++)
        {
            using var wrong = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "WrongPass1!"), JsonOptions);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        var unknown = $"nobody{Guid.NewGuid():N}@ama-stitches.test";
        using (var missing = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(unknown, "WrongPass1!"), JsonOptions))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        }

        using var ok = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Passw0rd!"), JsonOptions);
        var payload = (await ok.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;
        using (var logout = await _client.PostAsJsonAsync("/api/auth/logout", new LogoutRequest(payload.RefreshToken!), JsonOptions))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        var events = await EventsAsync(businessId);
        Assert.Single(events, e => e.Action == AuditActions.LoginFailed);
        Assert.Contains(events, e => e.Action == AuditActions.LoginSucceeded);
        Assert.Contains(events, e => e.Action == AuditActions.Logout);
        Assert.DoesNotContain(events, e => (e.Summary + e.ChangesJson).Contains("WrongPass", StringComparison.Ordinal));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.AuditEvents.IgnoreQueryFilters().AnyAsync(e => e.Summary.Contains(unknown)));
        Assert.True(await db.AuditEvents.IgnoreQueryFilters().AnyAsync(e => e.BusinessId == null && e.Action == AuditActions.LoginFailed));
    }

    [Fact]
    public async Task PaymentViews_AreRecordedOncePerHour()
    {
        var (token, businessId, _) = await RegisterAsync("View");
        var (_, orderId) = await CreateOrderAsync(token, 100m);
        await SendOk(HttpMethod.Get, $"/api/orders/{orderId}/payments", token);
        await SendOk(HttpMethod.Get, $"/api/orders/{orderId}/payments", token);
        await SendOk(HttpMethod.Get, "/api/payments", token);

        Assert.Single(await EventsAsync(businessId), e => e.Action == AuditActions.PaymentsViewed);
    }

    [Fact]
    public async Task RetriedSave_AfterAFailedOne_LogsOnlyWhatCommitted()
    {
        var (token, businessId, _) = await RegisterAsync("Retry");
        var (existingId, _) = await CreateOrderAsync(token, 100m);
        var freshId = IdGenerator.NewId();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.CurrentBusinessId = businessId;
            var duplicate = Customer.Create(existingId, businessId, "Duplicate Id", Phone, null, null, DateTimeOffset.UtcNow);
            db.Customers.Add(duplicate);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.Entry(duplicate).State = EntityState.Detached; // the caller recovers and saves something else
            db.Customers.Add(Customer.Create(freshId, businessId, "Fresh", Phone, null, null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var events = await EventsAsync(businessId);
        Assert.Single(events, e => e.Action == AuditActions.CustomerCreated && e.EntityId == freshId);
        Assert.DoesNotContain(events, e => e.Summary.Contains("Duplicate Id"));
    }

    [Fact]
    public async Task EmojiNameAtTheCutPoint_StillSaves()
    {
        var (token, businessId, _) = await RegisterAsync("Emoji");
        var name = new string('A', 58) + "👗👗 Mensah";
        using var response = await SendAsync(HttpMethod.Post, "/api/customers", token, new CreateCustomerRequest(name, Phone, null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains(await EventsAsync(businessId), e => e.Action == AuditActions.CustomerCreated && e.Summary.EndsWith("…"));
    }

    [Fact]
    public async Task EmptyPush_WritesNoRow()
    {
        var (token, businessId, _) = await RegisterAsync("Empty");
        await PushAsync(token, "quiet-phone", []);
        Assert.DoesNotContain(await EventsAsync(businessId), e => e.Action == AuditActions.SyncPush);
    }

    [Fact]
    public async Task Activity_RejectsBadInput_WithProblemDetails()
    {
        var (token, _, _) = await RegisterAsync("Bad");
        using var badDate = await SendAsync(HttpMethod.Get, "/api/audit?from=not-a-date", token);
        Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);
        Assert.Equal("application/problem+json", badDate.Content.Headers.ContentType?.MediaType);
        using var longSearch = await SendAsync(HttpMethod.Get, "/api/audit?search=" + new string('x', 101), token);
        Assert.Equal(HttpStatusCode.BadRequest, longSearch.StatusCode);
        var huge = await ListAsync(token, "page=2147483647&pageSize=100000");
        Assert.Empty(huge.Data);
        Assert.Equal(100, huge.PageSize);
    }

    private static SyncPushOperation CustomerOp(SyncCustomerDto customer, DateTimeOffset? baseModified = null) => new()
    {
        OpId = IdGenerator.NewId(),
        EntityType = SyncEntityType.Customer,
        EntityId = customer.Id,
        Customer = customer,
        BaseModifiedUtc = baseModified
    };

    private async Task<IReadOnlyList<SyncPushResult>> PushAsync(string token, string deviceId, List<SyncPushOperation> ops)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = Authorized(HttpMethod.Post, "/api/sync/push", token, new SyncPushRequest { Operations = ops, DeviceId = deviceId });
            using var response = await _client.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < 5)
            {
                await ApplicationDbContext.WaitForEpochBumpAsync().WaitAsync(TimeSpan.FromSeconds(60));
                await Task.Delay(50);
                continue;
            }

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = (await response.Content.ReadFromJsonAsync<SyncPushResponse>(JsonOptions))!;
            Assert.All(body.Results, r => Assert.True(r.Status == SyncOpStatus.Applied, $"{r.Code}: {r.Message}"));
            return body.Results;
        }
    }

    private async Task<SyncPullResponse> PullAsync(string token, string deviceId)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = Authorized(HttpMethod.Post, "/api/sync/pull", token, new SyncPullRequest { Limit = 500, DeviceId = deviceId });
            using var response = await _client.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < 5)
            {
                await ApplicationDbContext.WaitForEpochBumpAsync().WaitAsync(TimeSpan.FromSeconds(60));
                await Task.Delay(50);
                continue;
            }

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<SyncPullResponse>(JsonOptions))!;
        }
    }

    private async Task<List<AuditEvent>> EventsAsync(string businessId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuditEvents.IgnoreQueryFilters().AsNoTracking().Where(e => e.BusinessId == businessId).ToListAsync();
    }

    private async Task<PaginatedResponse<AuditEventDto>> ListAsync(string token, string query)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/audit?" + query, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PaginatedResponse<AuditEventDto>>(JsonOptions))!;
    }

    private async Task<PaymentDto> RecordAsync(string token, string orderId, decimal amount)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/payments", token,
            new RecordPaymentRequest { Amount = amount, Method = PaymentMethod.Cash });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PaymentDto>(JsonOptions))!;
    }

    private async Task SendOk(HttpMethod method, string url, string token, object? body = null)
    {
        using var response = await SendAsync(method, url, token, body);
        Assert.True(response.IsSuccessStatusCode, $"{method} {url} returned {(int)response.StatusCode}");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        using var request = Authorized(method, url, token, body);
        return await _client.SendAsync(request);
    }

    private async Task<(string CustomerId, string OrderId)> CreateOrderAsync(string token, decimal total)
    {
        using var customerResponse = await SendAsync(HttpMethod.Post, "/api/customers", token, new CreateCustomerRequest("Efua Mensah", Phone, null, null));
        Assert.Equal(HttpStatusCode.Created, customerResponse.StatusCode);
        var customer = (await customerResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions))!;
        using var response = await SendAsync(HttpMethod.Post, "/api/orders", token, new CreateOrderRequest
        {
            CustomerId = customer.Id,
            TotalAmount = total,
            Lines =
            [
                new CreateOrderLineRequest
                {
                    GarmentTemplate = GarmentTemplate.Dress, Quantity = 1, UnitPrice = total, Unit = MeasurementUnit.Centimetres,
                    MeasurementFields = new Dictionary<string, decimal?>()
                }
            ]
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (customer.Id, (await response.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions))!.Id);
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

    private async Task<(string AccessToken, string BusinessId, string Email)> RegisterAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"audit{label.ToLowerInvariant()}{suffix}@ama-stitches.test";
        var register = new RegisterBusinessRequest(email, "Passw0rd!", "Ama", "Owusu", $"Audit {label} {suffix}", "+233200000000");
        using var response = await _client.PostAsJsonAsync("/api/auth/register-business", register, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = (await response.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions))!;
        return (payload.AccessToken!, payload.User!.BusinessId!, email);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return request;
    }

    private sealed record AuthPayload(string? AccessToken, string? RefreshToken, AuthUserDto? User);
}
