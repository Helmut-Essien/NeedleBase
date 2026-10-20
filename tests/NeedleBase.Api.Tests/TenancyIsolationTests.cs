using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Tests;

/// <summary>G1: tenancy is fail-closed and cross-business access is impossible via the API.</summary>
public class TenancyIsolationTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly NeedleBaseApiFactory _factory;
    private readonly HttpClient _client;

    public TenancyIsolationTests(NeedleBaseApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task NoBusinessContext_QueryFilters_ReturnZeroRows()
    {
        var (token, _) = await RegisterAsync("N");
        var customer = await CreateCustomerAsync(token, "Fail Closed");
        await CreateMeasurementAsync(token, customer.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.CurrentBusinessId = null;

        Assert.Equal(0, await db.Customers.CountAsync());
        Assert.Equal(0, await db.MeasurementProfiles.CountAsync());
        Assert.Equal(0, await db.StaffMemberships.CountAsync());

        // Rows exist; only the explicit bypass can see them.
        Assert.True(await db.Customers.IgnoreQueryFilters().AnyAsync(c => c.Id == customer.Id));
    }

    [Fact]
    public async Task BusinessB_CannotReadUpdateOrDelete_BusinessA_Data()
    {
        var (tokenA, _) = await RegisterAsync("A");
        var (tokenB, _) = await RegisterAsync("B");
        var customer = await CreateCustomerAsync(tokenA, "Efua Mensah");
        var measurementId = await CreateMeasurementAsync(tokenA, customer.Id);

        var update = new UpdateCustomerRequest("Hijacked", "+233245550999", null, null);
        await AssertNotFound(HttpMethod.Get, $"/api/customers/{customer.Id}", tokenB);
        await AssertNotFound(HttpMethod.Put, $"/api/customers/{customer.Id}", tokenB, update);
        await AssertNotFound(HttpMethod.Delete, $"/api/customers/{customer.Id}", tokenB);
        await AssertNotFound(HttpMethod.Get, $"/api/customers/{customer.Id}/measurements", tokenB);
        await AssertNotFound(HttpMethod.Post, $"/api/customers/{customer.Id}/measurements", tokenB, MeasurementBody());
        await AssertNotFound(HttpMethod.Get, $"/api/measurements/{measurementId}", tokenB);
        await AssertNotFound(HttpMethod.Put, $"/api/measurements/{measurementId}", tokenB, UpdateMeasurementBody());
        await AssertNotFound(HttpMethod.Delete, $"/api/measurements/{measurementId}", tokenB);

        using var getA = Authorized(HttpMethod.Get, $"/api/customers/{customer.Id}", tokenA);
        using var getAResponse = await _client.SendAsync(getA);
        Assert.Equal(HttpStatusCode.OK, getAResponse.StatusCode);
        var stillA = await getAResponse.Content.ReadFromJsonAsync<CustomerDetailDto>(JsonOptions);
        Assert.Equal("Efua Mensah", stillA!.Name);

        using var getM = Authorized(HttpMethod.Get, $"/api/measurements/{measurementId}", tokenA);
        using var getMResponse = await _client.SendAsync(getM);
        Assert.Equal(HttpStatusCode.OK, getMResponse.StatusCode);
    }

    private async Task AssertNotFound(HttpMethod method, string url, string token, object? body = null)
    {
        using var request = Authorized(method, url, token, body);
        using var response = await _client.SendAsync(request);
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound,
            $"{method} {url} returned {(int)response.StatusCode}, expected 404.");
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

    private async Task<string> CreateMeasurementAsync(string token, string customerId)
    {
        using var request = Authorized(
            HttpMethod.Post,
            $"/api/customers/{customerId}/measurements",
            token,
            MeasurementBody());
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<MeasurementDetailDto>(JsonOptions);
        return created!.Id;
    }

    private static Dictionary<string, decimal?> Fields() => new()
    {
        ["bust"] = 92m,
        ["waist"] = 70m,
        ["hip"] = 98m,
        ["shoulder"] = 38m,
        ["sleeve_length"] = 58m,
        ["dress_length"] = 110m,
        ["neck"] = 34m
    };

    private static CreateMeasurementRequest MeasurementBody() => new()
    {
        GarmentTemplate = GarmentTemplate.Dress,
        Unit = MeasurementUnit.Centimetres,
        Fields = Fields()
    };

    private static UpdateMeasurementRequest UpdateMeasurementBody() => new()
    {
        GarmentTemplate = GarmentTemplate.Dress,
        Unit = MeasurementUnit.Centimetres,
        Fields = Fields()
    };

    private async Task<(string AccessToken, string BusinessId)> RegisterAsync(string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var register = new RegisterBusinessRequest(
            $"tenancy{label}{suffix}@ama-stitches.test",
            "Passw0rd!",
            "Ama",
            "Owusu",
            $"Tenancy {label} {suffix}",
            "+233200000000");
        using var response = await _client.PostAsJsonAsync("/api/auth/register-business", register, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions);
        return (payload!.AccessToken!, payload.User!.BusinessId!);
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
