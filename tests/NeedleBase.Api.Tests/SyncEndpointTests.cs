using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Api.Tests;

/// <summary>Slice 4: push idempotency, conflicts, tenancy, tombstones, cursor paging.</summary>
public partial class SyncEndpointTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _client;
    private readonly NeedleBaseApiFactory _factory;

    public SyncEndpointTests(NeedleBaseApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Sync_WithoutToken_IsUnauthorized()
    {
        using var response = await _client.PostAsJsonAsync("/api/sync/pull", new SyncPullRequest(), JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PushCreate_IsIdempotent_AndAppearsInPull()
    {
        var token = await RegisterAsync("Idem");
        var op = CustomerUpsert(Customer("Efua Mensah"), baseModified: null);

        var first = await PushAsync(token, op);
        var replay = await PushAsync(token, op); // same op again (lost ack / reopen)

        Assert.Equal(SyncOpStatus.Applied, first.Status);
        Assert.Equal(SyncOpStatus.Applied, replay.Status);
        Assert.Equal(first.Customer!.DateModifiedUtc, replay.Customer!.DateModifiedUtc);

        var all = await PullAllAsync(token);
        Assert.Single(all.Customers, c => c.Id == op.EntityId);
    }

    [Fact]
    public async Task StaleUpdate_IsConflict_AndServerIsNotOverwritten()
    {
        var token = await RegisterAsync("Conflict");
        var created = (await PushAsync(token, CustomerUpsert(Customer("Kofi"), null))).Customer!;

        // Device A edits from the current version: applied.
        var edit = await PushAsync(token, CustomerUpsert(created with { Name = "Kofi Annan" }, created.DateModifiedUtc));
        Assert.Equal(SyncOpStatus.Applied, edit.Status);

        // Device B still holds the old version: conflict, server copy returned, nothing overwritten.
        var stale = await PushAsync(token, CustomerUpsert(created with { Name = "Kofi B" }, created.DateModifiedUtc));
        Assert.Equal(SyncOpStatus.Conflict, stale.Status);
        Assert.Equal(SyncErrorCodes.StaleBase, stale.Code);
        Assert.Equal("Kofi Annan", stale.Customer!.Name);

        // A replayed create that differs from a changed server row is a conflict, not an overwrite.
        var staleCreate = await PushAsync(token, CustomerUpsert(created with { Name = "Kofi C" }, null));
        Assert.Equal(SyncOpStatus.Conflict, staleCreate.Status);

        // Stale delete is a conflict too.
        var staleDelete = await PushAsync(token, Delete(SyncEntityType.Customer, created.Id, created.DateModifiedUtc));
        Assert.Equal(SyncOpStatus.Conflict, staleDelete.Status);
    }

    [Fact]
    public async Task Pull_IsTenantScoped_AndForeignIdsAreNotReachable()
    {
        var tokenA = await RegisterAsync("TenantA");
        var tokenB = await RegisterAsync("TenantB");
        var a = (await PushAsync(tokenA, CustomerUpsert(Customer("Ama"), null))).Customer!;

        var pulledB = await PullAllAsync(tokenB);
        Assert.DoesNotContain(pulledB.Customers, c => c.Id == a.Id);

        // B can't update or delete A's record through push (not visible → treated as deleted/absent).
        var update = await PushAsync(tokenB, CustomerUpsert(a with { Name = "Hijack" }, a.DateModifiedUtc));
        Assert.NotEqual(SyncOpStatus.Applied, update.Status);
        var create = await PushAsync(tokenB, CustomerUpsert(a with { Name = "Hijack" }, null));
        Assert.Equal(SyncOpStatus.Failed, create.Status);
        Assert.Equal(SyncErrorCodes.Rejected, create.Code);

        var stillA = await PullAllAsync(tokenA);
        Assert.Equal("Ama", Assert.Single(stillA.Customers, c => c.Id == a.Id).Name);
    }

    [Fact]
    public async Task Delete_WritesTombstones_ForCascadedRows()
    {
        var token = await RegisterAsync("Tomb");
        var customer = (await PushAsync(token, CustomerUpsert(Customer("Esi"), null))).Customer!;
        var measurementId = IdGenerator.NewId();
        var measurement = await PushAsync(token, new SyncPushOperation
        {
            OpId = IdGenerator.NewId(),
            EntityType = SyncEntityType.Measurement,
            EntityId = measurementId,
            Measurement = new SyncMeasurementDto(measurementId, customer.Id, GarmentTemplate.Dress,
                MeasurementUnit.Centimetres, "{\"bust\":92}", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        });
        Assert.Equal(SyncOpStatus.Applied, measurement.Status);

        var before = await PullAllAsync(token);
        var deleted = await PushAsync(token, Delete(SyncEntityType.Customer, customer.Id, customer.DateModifiedUtc));
        Assert.Equal(SyncOpStatus.Applied, deleted.Status);

        var replay = await PushAsync(token, Delete(SyncEntityType.Customer, customer.Id, customer.DateModifiedUtc));
        Assert.Equal(SyncOpStatus.Applied, replay.Status); // idempotent delete

        var after = await PullAllAsync(token, before.Cursor);
        Assert.Contains(after.Deleted, d => d.EntityType == SyncEntityType.Customer && d.EntityId == customer.Id);
        Assert.Contains(after.Deleted, d => d.EntityType == SyncEntityType.Measurement && d.EntityId == measurementId);
    }

    [Fact]
    public async Task Pull_PagesByCursor_WithoutGaps()
    {
        var token = await RegisterAsync("Paging");
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add((await PushAsync(token, CustomerUpsert(Customer($"Page {i}"), null))).Customer!.Id);
        }

        var pages = 0;
        string? cursor = null;
        var seen = new HashSet<string>();
        SyncPullResponse page;
        do
        {
            page = await PullAsync(token, cursor, limit: 2);
            pages++;
            foreach (var c in page.Customers)
            {
                Assert.True(seen.Add(c.Id), "a row repeated within one sync");
            }

            cursor = page.Cursor;
        }
        while (page.HasMore);

        Assert.True(pages >= 3);
        Assert.All(ids, id => Assert.Contains(id, seen));

        // Next sync from the stored cursor works (may re-send the small overlap window).
        var next = await PullAsync(token, cursor, limit: 200);
        Assert.False(next.HasMore);

        using var bad = Authorized(HttpMethod.Post, "/api/sync/pull", token, new SyncPullRequest { Cursor = "not-a-cursor" });
        using var badResponse = await _client.SendAsync(bad);
        Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);
        using var doc = JsonDocument.Parse(await badResponse.Content.ReadAsStringAsync());
        Assert.Equal(SyncErrorCodes.CursorInvalid, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PushOrder_WithLinesAndStatus_IsIdempotent_AndPulledWithLines()
    {
        var token = await RegisterAsync("Order");
        var customer = (await PushAsync(token, CustomerUpsert(Customer("Yaa"), null))).Customer!;
        var orderId = IdGenerator.NewId();
        var now = DateTimeOffset.UtcNow;
        var order = new SyncOrderDto(orderId, customer.Id, Order0(orderId), OrderStatus.Ready, null, null, null, 300m, "kaba",
            now, now,
            [
                new SyncOrderLineDto(IdGenerator.NewId(), GarmentTemplate.Shirt, 2, 150m, null, MeasurementUnit.Centimetres, "{}", null, 0)
            ]);
        var op = new SyncPushOperation
        {
            OpId = IdGenerator.NewId(),
            EntityType = SyncEntityType.Order,
            EntityId = orderId,
            Order = order
        };

        var first = await PushAsync(token, op);
        Assert.Equal(SyncOpStatus.Applied, first.Status);
        Assert.Equal(OrderStatus.Ready, first.Order!.Status);
        var replay = await PushAsync(token, op);
        Assert.Equal(SyncOpStatus.Applied, replay.Status); // identical replay (lost ack): no-op
        Assert.Equal(OrderStatus.Ready, replay.Order!.Status);

        var pulled = await PullAllAsync(token);
        var pulledOrder = Assert.Single(pulled.Orders, o => o.Id == orderId);
        Assert.Single(pulledOrder.Lines);
        Assert.Equal(300m, pulledOrder.TotalAmount);

        // Device updates status from the server version: applied.
        var collect = await PushAsync(token, new SyncPushOperation
        {
            OpId = IdGenerator.NewId(),
            EntityType = SyncEntityType.Order,
            EntityId = orderId,
            BaseModifiedUtc = pulledOrder.DateModifiedUtc,
            Order = pulledOrder with { Status = OrderStatus.Collected }
        });
        Assert.Equal(SyncOpStatus.Applied, collect.Status);
        Assert.Equal(OrderStatus.Collected, collect.Order!.Status);
    }

    private static string Order0(string id) => id[^8..];

    private static SyncCustomerDto Customer(string name)
    {
        var now = DateTimeOffset.UtcNow;
        return new SyncCustomerDto(IdGenerator.NewId(), name, "+233245550123", null, null, now, now);
    }

    private static SyncPushOperation CustomerUpsert(SyncCustomerDto customer, DateTimeOffset? baseModified) => new()
    {
        OpId = IdGenerator.NewId(),
        EntityType = SyncEntityType.Customer,
        EntityId = customer.Id,
        BaseModifiedUtc = baseModified,
        Customer = customer
    };

    private static SyncPushOperation Delete(string type, string id, DateTimeOffset? baseModified) => new()
    {
        OpId = IdGenerator.NewId(),
        EntityType = type,
        EntityId = id,
        Operation = SyncOperation.Delete,
        BaseModifiedUtc = baseModified
    };

    private async Task<SyncPushResult> PushAsync(string token, SyncPushOperation op)
    {
        using var request = Authorized(HttpMethod.Post, "/api/sync/push", token, new SyncPushRequest { Operations = [op] });
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SyncPushResponse>(JsonOptions);
        var result = Assert.Single(body!.Results);
        Assert.Equal(op.OpId, result.OpId);
        return result;
    }

    private async Task<SyncPullResponse> PullAsync(string token, string? cursor, int limit)
    {
        var response = await SendPullAsync(token, cursor, limit);
        for (var attempt = 0; response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < 50; attempt++)
        {
            response.Dispose(); // background epoch re-stamp from an epoch test: wait it out like a device would
            await NeedleBase.Infrastructure.Persistence.ApplicationDbContext.WaitForEpochBumpAsync().WaitAsync(TimeSpan.FromSeconds(60));
            await Task.Delay(50);
            response = await SendPullAsync(token, cursor, limit);
        }

        using var _ = response;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SyncPullResponse>(JsonOptions))!;
    }

    private async Task<HttpResponseMessage> SendPullAsync(string token, string? cursor, int limit)
    {
        using var request = Authorized(HttpMethod.Post, "/api/sync/pull", token, new SyncPullRequest { Cursor = cursor, Limit = limit });
        return await _client.SendAsync(request);
    }

    /// <summary>Runs one full sync (all pages) and merges the pages.</summary>
    private async Task<SyncPullResponse> PullAllAsync(string token, string? cursor = null)
    {
        var customers = new List<SyncCustomerDto>();
        var measurements = new List<SyncMeasurementDto>();
        var orders = new List<SyncOrderDto>();
        var deleted = new List<SyncDeletedItemDto>();
        SyncPullResponse page;
        do
        {
            page = await PullAsync(token, cursor, 200);
            customers.AddRange(page.Customers);
            measurements.AddRange(page.Measurements);
            orders.AddRange(page.Orders);
            deleted.AddRange(page.Deleted);
            cursor = page.Cursor;
        }
        while (page.HasMore);

        return new SyncPullResponse(customers, measurements, orders, deleted, page.Cursor, false, page.ServerTimeUtc);
    }

    private async Task<string> RegisterAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var register = new RegisterBusinessRequest(
            $"sync{label}{suffix}@ama-stitches.test", "Passw0rd!", "Ama", "Owusu", $"Sync Biz {label} {suffix}", "+233200000000");
        using var response = await _client.PostAsJsonAsync("/api/auth/register-business", register, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("accessToken").GetString()!;
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
}
