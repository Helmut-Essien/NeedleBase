using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NeedleBase.Web.Tests;

/// <summary>
/// One site, one process (docs/deployment.md): with Api:InProcess the Web answers "/api/..." through the Api's own
/// pipeline (JSON health checks, ProblemDetails 404s) and keeps serving its pages everywhere else.
/// </summary>
public class InProcessApiTests : IClassFixture<InProcessApiTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Not Development (the Api would migrate a database) and not Production (strict settings check).
            builder.UseEnvironment("Testing");
            builder.UseSetting("Api:InProcess", "true");
            builder.UseSetting("Api:BaseUrl", "https://127.0.0.1:1/");
            // Nothing below reaches the database: live health, unknown routes and the sign-in page only.
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1");
            builder.UseSetting("JwtSettings:SecurityKey", "NeedleBase-Web-InProcess-Test-Key-32+chars");
            builder.UseSetting("Platform:RequireLicenseKey", "false");
            builder.ConfigureLogging(l => l.ClearProviders());
        }
    }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public InProcessApiTests(Factory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost/"),
            AllowAutoRedirect = false
        });
    }

    [Theory]
    [InlineData("/api/health/live")]
    [InlineData("/API/health/live")]
    public async Task Api_health_is_answered_by_the_api(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body?["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task Unknown_api_route_gets_the_apis_problem_details_not_a_web_page()
    {
        using var response = await _client.GetAsync("/api/no-such-endpoint");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("NOT_FOUND", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Api_without_a_token_is_401_not_a_cookie_sign_in_redirect()
    {
        using var response = await _client.GetAsync("/api/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Web_pages_still_come_from_the_web()
    {
        using var response = await _client.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Path_that_only_starts_with_api_stays_with_the_web()
    {
        using var response = await _client.GetAsync("/apiary");

        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Web_http_client_reaches_the_api_in_process_not_over_the_network()
    {
        // Api:BaseUrl is https://127.0.0.1:1/ — nothing listens there. With Api:InProcess the handler must answer
        // without going out to the network (otherwise this would hang/fail on connection refused).
        using var scope = _factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("AuthApi");
        using var response = await client.GetAsync("api/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body?["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task In_process_api_post_forwards_body_and_bearer_token()
    {
        using var scope = _factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("NeedleBaseApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        {
            Content = new StringContent("""{"email":"a@b.co","password":"x"}""", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer not-a-real-token");
        using var response = await client.SendAsync(request);

        // Reached the Api (validation/auth error JSON), not a network failure to :1 and not a Web HTML page.
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>The Web's appsettings.json carries the Api's defaults (the Api's own file is not deployed).</summary>
    [Fact]
    public void Web_settings_include_every_api_default()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src", "NeedleBase.Api", "appsettings.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var api = JsonNode.Parse(File.ReadAllText(Path.Combine(root.FullName, "src", "NeedleBase.Api", "appsettings.json")))!.AsObject();
        var web = JsonNode.Parse(File.ReadAllText(Path.Combine(root.FullName, "src", "NeedleBase.Web", "appsettings.json")))!.AsObject();
        foreach (var (key, value) in Flatten(api))
        {
            Assert.True(Flatten(web).TryGetValue(key, out var webValue), $"Web appsettings.json is missing {key}");
            Assert.Equal(value, webValue);
        }
    }

    private static Dictionary<string, string> Flatten(JsonObject obj, string prefix = "")
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in obj)
        {
            if (value is JsonObject child)
            {
                foreach (var pair in Flatten(child, prefix + key + ":"))
                {
                    result[pair.Key] = pair.Value;
                }
            }
            else
            {
                result[prefix + key] = value?.ToJsonString() ?? "null";
            }
        }

        return result;
    }
}
