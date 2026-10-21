namespace NeedleBase.Web.Services;

/// <summary>
/// Auth calls from the web server carry the browser's IP in X-Forwarded-For, so the API's per-IP auth limit and
/// per-email+IP login throttle apply to each visitor instead of to the web server as a whole. The API only trusts
/// this header from known proxies (loopback by default; ForwardedHeaders:KnownProxies / KnownNetworks).
/// </summary>
public sealed class ForwardClientIpHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ForwardClientIpHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var ip = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
        if (ip is not null)
        {
            request.Headers.Remove("X-Forwarded-For");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", ip.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
