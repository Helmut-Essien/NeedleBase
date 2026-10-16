using System.Text.Json;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace NeedleBase.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public IndexModel(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public string ApiBaseUrl { get; private set; } = string.Empty;
    public bool ApiReachable { get; private set; }
    public string? ApiHealthStatus { get; private set; }
    public MeProfile? Profile { get; private set; }
    public bool MeLoaded { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApiBaseUrl = _configuration["Api:BaseUrl"] ?? string.Empty;

        try
        {
            var client = _httpClientFactory.CreateClient("NeedleBaseApi");
            using var response = await client.GetAsync("health/live", cancellationToken);
            ApiReachable = response.IsSuccessStatusCode;
            if (ApiReachable)
            {
                var payload = await response.Content.ReadFromJsonAsync<HealthPayload>(cancellationToken);
                ApiHealthStatus = payload?.Status;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            ApiReachable = false;
        }
        catch (TaskCanceledException)
        {
            ApiReachable = false;
        }
        catch (JsonException)
        {
            ApiReachable = false;
        }

        if (User.Identity?.IsAuthenticated == true)
        {
            try
            {
                var api = _httpClientFactory.CreateClient("NeedleBaseApi");
                Profile = await api.GetFromJsonAsync<MeProfile>("api/me", cancellationToken);
                MeLoaded = Profile is not null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                MeLoaded = false;
            }
            catch (JsonException)
            {
                MeLoaded = false;
            }
        }
    }

    public sealed record MeProfile(
        string UserId,
        string BusinessId,
        string Role,
        string BusinessName);

    private sealed record HealthPayload(string Status);
}
