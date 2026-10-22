using System.Net;

namespace NeedleBase.Web.Handlers;

/// <summary>
/// Logs Api 401s. The redirect to login is done by the page (<c>AppPageModel.Fail</c> → LocalRedirect): writing the
/// response from here raced when a page's parallel calls all got 401. A 403 is a role answer the page shows.
/// </summary>
public sealed class UnauthorizedRedirectHandler : DelegatingHandler
{
    private readonly ILogger<UnauthorizedRedirectHandler> _logger;

    public UnauthorizedRedirectHandler(ILogger<UnauthorizedRedirectHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("API returned 401 for {RequestUri}", request.RequestUri);
        }

        return response;
    }
}
