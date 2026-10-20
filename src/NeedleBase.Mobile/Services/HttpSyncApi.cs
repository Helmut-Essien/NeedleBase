using System.Net;
using System.Net.Http.Json;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Mobile.Configuration;
using NeedleBase.Mobile.Data.Sync;
using Microsoft.Extensions.Options;

namespace NeedleBase.Mobile.Services;

/// <summary>Calls <c>POST api/sync/push</c> and <c>POST api/sync/pull</c> through the "ApiClient" (TokenRefreshHandler).</summary>
public sealed class HttpSyncApi : ISyncApi
{
    public const string ClientName = "ApiClient";
    private readonly IHttpClientFactory _factory;
    private readonly ApiSettings _settings;

    public HttpSyncApi(IHttpClientFactory factory, IOptions<ApiSettings> settings)
    {
        _factory = factory;
        _settings = settings.Value;
    }

    public Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken) =>
        PostAsync<SyncPushRequest, SyncPushResponse>("api/sync/push", request, cancellationToken);

    public Task<SyncPullResponse> PullAsync(SyncPullRequest request, CancellationToken cancellationToken) =>
        PostAsync<SyncPullRequest, SyncPullResponse>("api/sync/pull", request, cancellationToken);

    public static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        if (header?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }

    private static string? ReadCode(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
    {
        var client = _factory.CreateClient(ClientName);
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(new Uri(new Uri(_settings.BaseUrl), path), body, SyncJson.Options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new SyncTransientException("Can't reach NeedleBase.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SyncTransientException("NeedleBase took too long to answer.", ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<TResponse>(SyncJson.Options, cancellationToken).ConfigureAwait(false)
                    ?? throw new SyncTransientException("Empty sync response.");
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var code = ReadCode(text);
                if (code == SyncErrorCodes.EpochChanged)
                {
                    throw new SyncCursorInvalidException(serverReset: true);
                }

                if (code == SyncErrorCodes.CursorInvalid || text.Contains(SyncErrorCodes.CursorInvalid, StringComparison.Ordinal))
                {
                    throw new SyncCursorInvalidException();
                }

                // Any other 400 won't succeed by retrying the same request (usually an outdated app).
                throw new SyncRejectedException(ReadCode(text));
            }

            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    throw new SyncSessionExpiredException();
                case HttpStatusCode.Forbidden:
                    throw new SyncAccessDeniedException();
            }

            // 5xx, 429, other 4xx: keep the queue and back off (at least as long as Retry-After says).
            throw new SyncTransientException($"Sync failed ({(int)response.StatusCode}).", retryAfter: RetryAfter(response),
                rateLimited: response.StatusCode == HttpStatusCode.TooManyRequests);
        }
    }
}
