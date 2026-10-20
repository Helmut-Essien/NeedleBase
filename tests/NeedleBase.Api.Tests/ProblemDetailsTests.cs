using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;

namespace NeedleBase.Api.Tests;

/// <summary>G5: every error is RFC 9457 ProblemDetails with a stable machine <c>code</c>.</summary>
public class ProblemDetailsTests : IClassFixture<NeedleBaseApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _client;

    public ProblemDetailsTests(NeedleBaseApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task NotFound_Customer_ReturnsProblemWithCode()
    {
        var token = await RegisterAsync();
        using var request = Authorized(HttpMethod.Get, "/api/customers/01JZZZZZZZZZZZZZZZZZZZZZZZ", token);
        using var response = await _client.SendAsync(request);

        await AssertProblem(response, HttpStatusCode.NotFound, "CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task Conflict_CustomerIdOwnedByAnotherBusiness_ReturnsProblemWithCode()
    {
        var tokenA = await RegisterAsync();
        var tokenB = await RegisterAsync();
        var id = NeedleBase.Application.Common.IdGenerator.NewId();

        using (var createA = Authorized(HttpMethod.Post, "/api/customers", tokenA,
                   new CreateCustomerRequest("Efua Mensah", "+233245550123", null, null, id)))
        using (var responseA = await _client.SendAsync(createA))
        {
            Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        }

        using var createB = Authorized(HttpMethod.Post, "/api/customers", tokenB,
            new CreateCustomerRequest("Kwame Asante", "+233245550124", null, null, id));
        using var responseB = await _client.SendAsync(createB);

        await AssertProblem(responseB, HttpStatusCode.Conflict, "CUSTOMER_CONFLICT");
    }

    [Fact]
    public async Task Validation_MissingName_ReturnsValidationProblemWithErrors()
    {
        var token = await RegisterAsync();
        using var request = Authorized(HttpMethod.Post, "/api/customers", token,
            new CreateCustomerRequest(string.Empty, "+233245550123", null, null));
        using var response = await _client.SendAsync(request);

        var root = await AssertProblem(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        Assert.True(root.TryGetProperty("errors", out var errors));
        Assert.Equal(JsonValueKind.Object, errors.ValueKind);
        Assert.True(errors.EnumerateObject().Any());
    }

    [Fact]
    public async Task Unauthorized_NoToken_ReturnsProblemWithCode()
    {
        using var response = await _client.GetAsync("/api/customers");

        await AssertProblem(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement.Clone();
        Assert.Equal((int)status, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.Equal(code, root.GetProperty("code").GetString());
        Assert.False(root.TryGetProperty("message", out _));
        return root;
    }

    private async Task<string> RegisterAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var register = new RegisterBusinessRequest(
            $"problem{suffix}@ama-stitches.test",
            "Passw0rd!",
            "Ama",
            "Owusu",
            $"Problem Shop {suffix}",
            "+233200000000");
        using var response = await _client.PostAsJsonAsync("/api/auth/register-business", register, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuthPayload>(JsonOptions);
        return payload!.AccessToken!;
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
