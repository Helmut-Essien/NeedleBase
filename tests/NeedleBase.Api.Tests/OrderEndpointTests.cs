using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Api.Tests;

public partial class OrderEndpointTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _client;

    public OrderEndpointTests(NeedleBaseApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Orders_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Order_CrudAndStatus_IsBusinessScoped()
    {
        var (tokenA, businessA) = await RegisterAsync("A");
        var (tokenB, _) = await RegisterAsync("B");
        var customer = await CreateCustomerAsync(tokenA, "Efua Mensah");

        var createBody = new CreateOrderRequest
        {
            CustomerId = customer.Id,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            TotalAmount = 800m,
            Notes = "Slit & kaba",
            Lines =
            [
                new CreateOrderLineRequest
                {
                    GarmentTemplate = GarmentTemplate.Dress,
                    Quantity = 1,
                    UnitPrice = 800m,
                    Unit = MeasurementUnit.Centimetres,
                    MeasurementFields = new Dictionary<string, decimal?> { ["bust"] = 92m, ["waist"] = 70m }
                }
            ]
        };

        using var createRequest = Authorized(HttpMethod.Post, "/api/orders", tokenA, createBody);
        using var createResponse = await _client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(businessA, created.BusinessId);
        Assert.Equal(OrderStatus.New, created.Status);
        Assert.Equal("Ready to collect", OrderStatus.DisplayName(OrderStatus.Ready));
        Assert.NotEmpty(created.DisplayRef);
        Assert.Single(created.Lines);
        Assert.Equal(92m, created.Lines[0].MeasurementFields["bust"]);

        using var listA = Authorized(HttpMethod.Get, "/api/orders?page=1&pageSize=20", tokenA);
        using var listAResponse = await _client.SendAsync(listA);
        Assert.Equal(HttpStatusCode.OK, listAResponse.StatusCode);
        var pageA = await listAResponse.Content.ReadFromJsonAsync<PaginatedResponse<OrderListItemDto>>(JsonOptions);
        Assert.Contains(pageA!.Data, o => o.Id == created.Id);

        using var listB = Authorized(HttpMethod.Get, "/api/orders?page=1&pageSize=20", tokenB);
        using var listBResponse = await _client.SendAsync(listB);
        Assert.Equal(HttpStatusCode.OK, listBResponse.StatusCode);
        var pageB = await listBResponse.Content.ReadFromJsonAsync<PaginatedResponse<OrderListItemDto>>(JsonOptions);
        Assert.DoesNotContain(pageB!.Data, o => o.Id == created.Id);

        using var getB = Authorized(HttpMethod.Get, $"/api/orders/{created.Id}", tokenB);
        using var getBResponse = await _client.SendAsync(getB);
        Assert.Equal(HttpStatusCode.NotFound, getBResponse.StatusCode);

        using var statusRequest = Authorized(
            HttpMethod.Patch,
            $"/api/orders/{created.Id}/status",
            tokenA,
            new UpdateOrderStatusRequest { Status = OrderStatus.Cutting });
        using var statusResponse = await _client.SendAsync(statusRequest);
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var updated = await statusResponse.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions);
        Assert.Equal(OrderStatus.Cutting, updated!.Status);

        using var collectRequest = Authorized(
            HttpMethod.Patch,
            $"/api/orders/{created.Id}/status",
            tokenA,
            new UpdateOrderStatusRequest { Status = OrderStatus.Collected });
        using var collectResponse = await _client.SendAsync(collectRequest);
        Assert.Equal(HttpStatusCode.OK, collectResponse.StatusCode);

        using var reopenRequest = Authorized(
            HttpMethod.Patch,
            $"/api/orders/{created.Id}/status",
            tokenA,
            new UpdateOrderStatusRequest { Status = OrderStatus.Sewing });
        using var reopenResponse = await _client.SendAsync(reopenRequest);
        Assert.Equal(HttpStatusCode.Conflict, reopenResponse.StatusCode);
        Assert.Equal("ORDER_LOCKED", await ReadCodeAsync(reopenResponse));

        using var editTerminal = Authorized(
            HttpMethod.Put,
            $"/api/orders/{created.Id}",
            tokenA,
            new UpdateOrderRequest { Notes = "late edit" });
        using var editTerminalResponse = await _client.SendAsync(editTerminal);
        Assert.Equal(HttpStatusCode.Conflict, editTerminalResponse.StatusCode);
        Assert.Equal("ORDER_LOCKED", await ReadCodeAsync(editTerminalResponse));
    }

    [Fact]
    public async Task CreateOrder_WithClientUlid_IsIdempotent()
    {
        var (token, _) = await RegisterAsync("Idem");
        var customer = await CreateCustomerAsync(token, "Kwame");
        var clientId = IdGenerator.NewId();
        var body = new CreateOrderRequest
        {
            Id = clientId,
            CustomerId = customer.Id,
            TotalAmount = 100m,
            Lines =
            [
                new CreateOrderLineRequest
                {
                    GarmentTemplate = GarmentTemplate.Shirt,
                    Quantity = 1,
                    UnitPrice = 100m,
                    Unit = MeasurementUnit.Centimetres,
                    MeasurementFields = new Dictionary<string, decimal?>()
                }
            ]
        };

        using var first = Authorized(HttpMethod.Post, "/api/orders", token, body);
        using var firstResponse = await _client.SendAsync(first);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using var second = Authorized(HttpMethod.Post, "/api/orders", token, body);
        using var secondResponse = await _client.SendAsync(second);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var again = await secondResponse.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions);
        Assert.Equal(clientId, again!.Id);
    }

    [Fact]
    public async Task UpdateStatus_UnknownStatus_Returns400StatusInvalid()
    {
        var (token, _) = await RegisterAsync("Status");
        var customer = await CreateCustomerAsync(token, "Abena");
        var order = await CreateOrderAsync(token, OneLine(customer.Id, 100m));

        using var request = Authorized(
            HttpMethod.Patch,
            $"/api/orders/{order.Id}/status",
            token,
            new UpdateOrderStatusRequest { Status = "Teleported" });
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ORDER_STATUS_INVALID", await ReadCodeAsync(response));
    }

    [Fact]
    public async Task CreateOrder_TotalIsComputedFromLines_AndMismatchIsRejected()
    {
        var (token, _) = await RegisterAsync("Total");
        var customer = await CreateCustomerAsync(token, "Yaw");

        var body = OneLine(customer.Id, 150.5m, quantity: 2);
        body.TotalAmount = null;
        var created = await CreateOrderAsync(token, body);
        Assert.Equal(301m, created.TotalAmount);
        Assert.Equal(8, created.DisplayRef.Length);

        var bad = OneLine(customer.Id, 100m);
        bad.TotalAmount = 999m;
        using var request = Authorized(HttpMethod.Post, "/api/orders", token, bad);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ORDER_TOTAL_MISMATCH", await ReadCodeAsync(response));
    }

    [Fact]
    public async Task CreateOrder_IgnoresClientLineIds()
    {
        var (token, _) = await RegisterAsync("LineIds");
        var customer = await CreateCustomerAsync(token, "Esi");
        var first = await CreateOrderAsync(token, OneLine(customer.Id, 100m));
        var clientLineId = first.Lines[0].Id;

        // Raw payload carrying a line id that already exists; the server must ignore it.
        var raw = new
        {
            customerId = customer.Id,
            lines = new[]
            {
                new
                {
                    id = clientLineId,
                    garmentTemplate = GarmentTemplate.Shirt,
                    quantity = 1,
                    unitPrice = 100m,
                    unit = MeasurementUnit.Centimetres,
                    measurementFields = new Dictionary<string, decimal?>()
                }
            }
        };
        using var request = Authorized(HttpMethod.Post, "/api/orders", token, raw);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var second = await response.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions);
        Assert.NotEqual(clientLineId, second!.Lines[0].Id);
    }

    [Fact]
    public async Task CreateOrder_AssigneeMustBeActiveMember()
    {
        var (token, _, userId) = await RegisterWithUserAsync("Assign");
        var customer = await CreateCustomerAsync(token, "Kofi");

        var bad = OneLine(customer.Id, 100m);
        bad.AssignedToUserId = IdGenerator.NewId();
        using var request = Authorized(HttpMethod.Post, "/api/orders", token, bad);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ASSIGNEE_INVALID", await ReadCodeAsync(response));

        var good = OneLine(customer.Id, 100m);
        good.AssignedToUserId = userId;
        var created = await CreateOrderAsync(token, good);
        Assert.Equal(userId, created.AssignedToUserId);
    }

    [Fact]
    public async Task ListOrders_SearchIsCaseInsensitive()
    {
        var (token, _) = await RegisterAsync("Search");
        var customer = await CreateCustomerAsync(token, "Akosua Boateng");
        var created = await CreateOrderAsync(token, OneLine(customer.Id, 100m));

        foreach (var term in new[] { "akosua", "BOATENG", "#" + created.DisplayRef.ToLowerInvariant() })
        {
            using var request = Authorized(
                HttpMethod.Get,
                $"/api/orders?page=1&pageSize=20&search={Uri.EscapeDataString(term)}",
                token);
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await response.Content.ReadFromJsonAsync<PaginatedResponse<OrderListItemDto>>(JsonOptions);
            Assert.Contains(page!.Data, o => o.Id == created.Id);
        }
    }

    [Fact]
    public async Task DeleteCustomer_WithOrders_Returns409CustomerHasOrders()
    {
        var (token, _) = await RegisterAsync("CustDel");
        var withOrders = await CreateCustomerAsync(token, "Ama Serwaa");
        await CreateOrderAsync(token, OneLine(withOrders.Id, 100m));
        var withoutOrders = await CreateCustomerAsync(token, "Adwoa");

        using var blocked = Authorized(HttpMethod.Delete, $"/api/customers/{withOrders.Id}", token);
        using var blockedResponse = await _client.SendAsync(blocked);
        Assert.Equal(HttpStatusCode.Conflict, blockedResponse.StatusCode);
        Assert.Equal("CUSTOMER_HAS_ORDERS", await ReadCodeAsync(blockedResponse));

        using var allowed = Authorized(HttpMethod.Delete, $"/api/customers/{withoutOrders.Id}", token);
        using var allowedResponse = await _client.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.NoContent, allowedResponse.StatusCode);

        using var missing = Authorized(HttpMethod.Delete, $"/api/customers/{IdGenerator.NewId()}", token);
        using var missingResponse = await _client.SendAsync(missing);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    private static CreateOrderRequest OneLine(string customerId, decimal unitPrice, int quantity = 1) => new()
    {
        CustomerId = customerId,
        TotalAmount = unitPrice * quantity,
        Lines =
        [
            new CreateOrderLineRequest
            {
                GarmentTemplate = GarmentTemplate.Shirt,
                Quantity = quantity,
                UnitPrice = unitPrice,
                Unit = MeasurementUnit.Centimetres,
                MeasurementFields = new Dictionary<string, decimal?>()
            }
        ]
    };

    private async Task<OrderDetailDto> CreateOrderAsync(string token, CreateOrderRequest body)
    {
        using var request = Authorized(HttpMethod.Post, "/api/orders", token, body);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions))!;
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<CustomerDetailDto> CreateCustomerAsync(string token, string name)
    {
        using var request = Authorized(
            HttpMethod.Post,
            "/api/customers",
            token,
            new CreateCustomerRequest(name, "+233245550123", null, null));
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions))!;
    }

    private async Task<(string AccessToken, string BusinessId)> RegisterAsync(string label)
    {
        var (token, businessId, _) = await RegisterWithUserAsync(label);
        return (token, businessId);
    }

    private async Task<(string AccessToken, string BusinessId, string UserId)> RegisterWithUserAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var register = new RegisterBusinessRequest(
            $"order{label}{suffix}@ama-stitches.test",
            "Passw0rd!",
            "Ama",
            "Owusu",
            $"Order Biz {label} {suffix}",
            "+233200000000");
        using var response = await _client.PostAsJsonAsync("/api/auth/register-business", register, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions);
        return (payload!.AccessToken!, payload.User!.BusinessId!, payload.User.UserId);
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
