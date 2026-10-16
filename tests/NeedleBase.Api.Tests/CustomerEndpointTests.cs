using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Api.Tests;

public class CustomerEndpointTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _client;

    public CustomerEndpointTests(NeedleBaseApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Customers_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/customers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CustomerAndMeasurement_Crud_IsBusinessScoped()
    {
        var (tokenA, businessA) = await RegisterAsync("A");
        var (tokenB, _) = await RegisterAsync("B");

        using var createRequest = Authorized(
            HttpMethod.Post,
            "/api/customers",
            tokenA,
            new CreateCustomerRequest("Efua Mensah", "+233245550123", "Sample", null));
        using var createResponse = await _client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(businessA, created.BusinessId);
        Assert.Equal("Efua Mensah", created.Name);

        using var listA = Authorized(HttpMethod.Get, "/api/customers?page=1&pageSize=20", tokenA);
        using var listAResponse = await _client.SendAsync(listA);
        Assert.Equal(HttpStatusCode.OK, listAResponse.StatusCode);
        var pageA = await listAResponse.Content.ReadFromJsonAsync<PaginatedResponse<CustomerListItemDto>>(JsonOptions);
        Assert.NotNull(pageA);
        Assert.Contains(pageA.Data, c => c.Id == created.Id);

        using var listB = Authorized(HttpMethod.Get, "/api/customers?page=1&pageSize=20", tokenB);
        using var listBResponse = await _client.SendAsync(listB);
        Assert.Equal(HttpStatusCode.OK, listBResponse.StatusCode);
        var pageB = await listBResponse.Content.ReadFromJsonAsync<PaginatedResponse<CustomerListItemDto>>(JsonOptions);
        Assert.NotNull(pageB);
        Assert.DoesNotContain(pageB.Data, c => c.Id == created.Id);

        using var getB = Authorized(HttpMethod.Get, $"/api/customers/{created.Id}", tokenB);
        using var getBResponse = await _client.SendAsync(getB);
        Assert.Equal(HttpStatusCode.NotFound, getBResponse.StatusCode);

        var measurementBody = new CreateMeasurementRequest
        {
            GarmentTemplate = GarmentTemplate.Dress,
            Unit = MeasurementUnit.Centimetres,
            Fields = new Dictionary<string, decimal?>
            {
                ["bust"] = 92m,
                ["waist"] = 70m,
                ["hip"] = 98m,
                ["shoulder"] = 38m,
                ["sleeve_length"] = 58m,
                ["dress_length"] = 110m,
                ["neck"] = 34m
            },
            Notes = "Dress fitting"
        };

        using var measureRequest = Authorized(
            HttpMethod.Post,
            $"/api/customers/{created.Id}/measurements",
            tokenA,
            measurementBody);
        using var measureResponse = await _client.SendAsync(measureRequest);
        Assert.Equal(HttpStatusCode.Created, measureResponse.StatusCode);
        var measurement = await measureResponse.Content.ReadFromJsonAsync<MeasurementDetailDto>(JsonOptions);
        Assert.NotNull(measurement);
        Assert.Equal(GarmentTemplate.Dress, measurement.GarmentTemplate);
        Assert.Equal(92m, measurement.Fields["bust"]);

        using var history = Authorized(
            HttpMethod.Get,
            $"/api/customers/{created.Id}/measurements",
            tokenA);
        using var historyResponse = await _client.SendAsync(history);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var historyPage = await historyResponse.Content
            .ReadFromJsonAsync<PaginatedResponse<MeasurementListItemDto>>(JsonOptions);
        Assert.NotNull(historyPage);
        Assert.Contains(historyPage.Data, m => m.Id == measurement.Id);
    }

    [Fact]
    public async Task CreateCustomer_WithClientUlid_IsIdempotent()
    {
        var (token, _) = await RegisterAsync("Idem");
        var clientId = IdGenerator.NewId();

        using var first = Authorized(
            HttpMethod.Post,
            "/api/customers",
            token,
            new CreateCustomerRequest("Efua Mensah", "+233245550123", null, null, clientId));
        using var firstResponse = await _client.SendAsync(first);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var created = await firstResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions);
        Assert.Equal(clientId, created!.Id);

        using var second = Authorized(
            HttpMethod.Post,
            "/api/customers",
            token,
            new CreateCustomerRequest("Different Name", "+233200000000", null, null, clientId));
        using var secondResponse = await _client.SendAsync(second);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var replay = await secondResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions);
        Assert.Equal(clientId, replay!.Id);
        Assert.Equal("Efua Mensah", replay.Name);

        using var list = Authorized(HttpMethod.Get, "/api/customers?search=Efua", token);
        using var listResponse = await _client.SendAsync(list);
        var page = await listResponse.Content.ReadFromJsonAsync<PaginatedResponse<CustomerListItemDto>>(JsonOptions);
        Assert.Equal(1, page!.Data.Count(c => c.Id == clientId));
    }

    [Fact]
    public async Task Measurements_ForUnknownCustomer_ReturnsNotFound()
    {
        var (token, _) = await RegisterAsync("Miss");
        using var request = Authorized(
            HttpMethod.Get,
            "/api/customers/01HZMISSINGCUSTOMER00001/measurements",
            token);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(string AccessToken, string BusinessId)> RegisterAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var register = new RegisterBusinessRequest(
            $"owner{label}{suffix}@ama-stitches.test",
            "Passw0rd!",
            "Ama",
            "Owusu",
            $"Ama Stitches {label} {suffix}",
            "+233200000000");

        using var response = await _client.PostAsJsonAsync("/api/auth/register-business", register, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions);
        Assert.NotNull(payload?.AccessToken);
        Assert.NotNull(payload.User?.BusinessId);
        return (payload.AccessToken, payload.User.BusinessId);
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
