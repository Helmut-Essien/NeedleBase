using System.Net;
using System.Net.Http.Json;

namespace NeedleBase.Api.Tests;

public class HealthEndpointTests : IClassFixture<NeedleBaseApiFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(NeedleBaseApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthLive_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HealthPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Healthy", payload.Status);
    }

    [Fact]
    public async Task HealthReady_ReturnsHealthyWhenDatabaseReachable()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HealthPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Healthy", payload.Status);
    }

    [Fact]
    public async Task Ping_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/test/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record HealthPayload(string Status);
}
