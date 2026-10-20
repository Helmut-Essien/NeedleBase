using System.Net;
using System.Net.Http.Headers;
using NeedleBase.Mobile.Data.Sync;

namespace NeedleBase.Mobile.Services;

/// <summary>
/// Adds the bearer token to API calls. On 401 it refreshes once (through the app-wide refresh lock in
/// <see cref="IAuthService.RefreshIfStaleAsync"/>, so concurrent 401s share one refresh) and retries.
/// A transient refresh failure surfaces as <see cref="SyncTransientException"/> (keep session + queue);
/// a rejected refresh as <see cref="SyncSessionExpiredException"/>. It never wipes data.
/// </summary>
public sealed class TokenRefreshHandler : DelegatingHandler
{
    private readonly IAuthService _auth;

    public TokenRefreshHandler(IAuthService auth) => _auth = auth;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _auth.GetAccessTokenAsync().ConfigureAwait(false);
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var contentType = request.Content?.Headers.ContentType;
        SetBody(request, body, contentType);
        Authorize(request, token);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var refresh = await _auth.RefreshIfStaleAsync(token, cancellationToken).ConfigureAwait(false);
        if (!refresh.Succeeded)
        {
            if (refresh.IsTransientFailure)
            {
                throw new SyncTransientException(refresh.Message);
            }

            throw new SyncSessionExpiredException();
        }

        using var retry = new HttpRequestMessage(request.Method, request.RequestUri);
        SetBody(retry, body, contentType);
        Authorize(retry, await _auth.GetAccessTokenAsync().ConfigureAwait(false));
        var second = await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
        if (second.StatusCode == HttpStatusCode.Unauthorized)
        {
            second.Dispose();
            throw new SyncSessionExpiredException();
        }

        return second;
    }

    private static void Authorize(HttpRequestMessage request, string? token)
    {
        request.Headers.Authorization = string.IsNullOrWhiteSpace(token) ? null : new AuthenticationHeaderValue("Bearer", token);
    }

    private static void SetBody(HttpRequestMessage request, byte[]? body, MediaTypeHeaderValue? contentType)
    {
        if (body is null)
        {
            return;
        }

        var content = new ByteArrayContent(body);
        content.Headers.ContentType = contentType;
        request.Content = content;
    }
}
