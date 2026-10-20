using Microsoft.AspNetCore.Mvc;

namespace NeedleBase.Api.Http;

/// <summary>
/// RFC 9457 ProblemDetails factory. <c>title</c>/<c>detail</c> are user-safe copy;
/// <c>code</c> (extension member) is the stable machine identifier clients switch on.
/// Never put exception text or internal claim names in these fields.
/// </summary>
public static class ApiProblems
{
    public const string CodeKey = "code";

    public static ObjectResult Create(HttpContext httpContext, int status, string code, string title, string? detail = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}",
            Instance = httpContext.Request.Path
        };
        problem.Extensions[CodeKey] = code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }

    public static ObjectResult CustomerNotFound(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status404NotFound, "CUSTOMER_NOT_FOUND", "Customer not found",
            "We couldn't find that customer. It may have been deleted.");

    public static ObjectResult MeasurementNotFound(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status404NotFound, "MEASUREMENT_NOT_FOUND", "Measurement not found",
            "We couldn't find that measurement. It may have been deleted.");

    public static ObjectResult CustomerConflict(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status409Conflict, "CUSTOMER_CONFLICT", "Customer already exists",
            "This customer has already been saved.");

    public static ObjectResult MeasurementConflict(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status409Conflict, "MEASUREMENT_CONFLICT", "Measurement already exists",
            "This measurement has already been saved.");

    public static ObjectResult SaveFailed(HttpContext ctx, string code, string what) =>
        Create(ctx, StatusCodes.Status400BadRequest, code, $"Couldn't save {what}",
            $"We couldn't save the {what}. Check the details and try again.");

    public static ObjectResult SessionInvalid(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status401Unauthorized, "SESSION_INVALID", "Please sign in again",
            "Your session is no longer valid. Please sign in again.");

    public static ObjectResult BusinessNotFound(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status404NotFound, "BUSINESS_MISSING", "Business not found",
            "We couldn't find your business. Please sign in again.");
}
