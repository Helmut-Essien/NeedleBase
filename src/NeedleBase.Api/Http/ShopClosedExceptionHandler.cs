using Microsoft.AspNetCore.Diagnostics;
using NeedleBase.Application.Contracts.Auth;

namespace NeedleBase.Api.Http;

/// <summary>Maps a write against a closed shop to 409 SHOP_CLOSED instead of a 500.</summary>
internal sealed class ShopClosedExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ShopClosedException)
        {
            return false;
        }

        var result = ApiProblems.Create(
            httpContext,
            StatusCodes.Status409Conflict,
            "SHOP_CLOSED",
            "This shop account has been deleted",
            "This shop account has been deleted.");
        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await result.ExecuteResultAsync(new Microsoft.AspNetCore.Mvc.ActionContext(
            httpContext,
            httpContext.GetRouteData() ?? new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        return true;
    }
}
