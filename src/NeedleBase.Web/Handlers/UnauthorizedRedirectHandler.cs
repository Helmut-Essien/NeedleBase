using System.Net;

namespace NeedleBase.Web.Handlers;

public sealed class UnauthorizedRedirectHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<UnauthorizedRedirectHandler> _logger;

    public UnauthorizedRedirectHandler(
        IHttpContextAccessor httpContextAccessor,
        ILogger<UnauthorizedRedirectHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
        {
            return response;
        }

        _logger.LogWarning(
            "API returned {StatusCode} for {RequestUri}",
            response.StatusCode,
            request.RequestUri);

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null && !httpContext.Response.HasStarted)
        {
            var returnUrl = httpContext.Request.Path + httpContext.Request.QueryString;
            var loginUrl = $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}";
            httpContext.Response.Redirect(loginUrl);
        }

        return response;
    }
}
