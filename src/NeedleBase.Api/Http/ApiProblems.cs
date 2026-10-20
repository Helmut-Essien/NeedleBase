using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Orders;

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

    public static ObjectResult CustomerHasOrders(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status409Conflict, "CUSTOMER_HAS_ORDERS", "Customer has orders",
            "This customer has orders. Finish or cancel them before deleting the customer.");

    /// <summary>Single mapping from <see cref="OrderError"/> to status + stable code + friendly detail.</summary>
    public static ObjectResult ForOrder(HttpContext ctx, OrderError error)
    {
        var (status, code, title) = error switch
        {
            OrderError.NotFound => (StatusCodes.Status404NotFound, OrderErrorMessages.OrderNotFound, "Order not found"),
            OrderError.CustomerNotFound => (StatusCodes.Status404NotFound, OrderErrorMessages.CustomerNotFound, "Customer not found"),
            OrderError.MeasurementNotFound => (StatusCodes.Status404NotFound, OrderErrorMessages.MeasurementNotFound, "Measurement not found"),
            OrderError.IdConflict => (StatusCodes.Status409Conflict, OrderErrorMessages.OrderConflict, "Order already exists"),
            OrderError.Locked => (StatusCodes.Status409Conflict, OrderErrorMessages.OrderLocked, "Order can't be changed"),
            OrderError.StatusInvalid => (StatusCodes.Status400BadRequest, OrderErrorMessages.OrderStatusInvalid, "Unknown status"),
            OrderError.AssigneeInvalid => (StatusCodes.Status400BadRequest, OrderErrorMessages.AssigneeInvalid, "Assignee not on this team"),
            OrderError.TotalMismatch => (StatusCodes.Status400BadRequest, OrderErrorMessages.TotalMismatch, "Total doesn't match"),
            _ => (StatusCodes.Status400BadRequest, OrderErrorMessages.Validation, "Check the order details")
        };

        return Create(ctx, status, code, title, OrderErrorMessages.For(code));
    }

    public static ObjectResult OrderNotFound(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status404NotFound, "ORDER_NOT_FOUND", "Order not found",
            "We couldn't find that order. It may have been deleted.");

    public static ObjectResult OrderConflict(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status409Conflict, "ORDER_CONFLICT", "Order already exists",
            "This order has already been saved.");

    public static ObjectResult SaveFailed(HttpContext ctx, string code, string what) =>
        Create(ctx, StatusCodes.Status400BadRequest, code, $"Couldn't save {what}",
            $"We couldn't save the {what}. Check the details and try again.");

    public static ObjectResult SyncCursorInvalid(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status400BadRequest, NeedleBase.Application.Contracts.Sync.SyncErrorCodes.CursorInvalid,
            "Sync needs to start again", "Your device will download your data again. Please try syncing once more.");

    public static ObjectResult SessionInvalid(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status401Unauthorized, "SESSION_INVALID", "Please sign in again",
            "Your session is no longer valid. Please sign in again.");

    public static ObjectResult BusinessNotFound(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status404NotFound, "BUSINESS_MISSING", "Business not found",
            "We couldn't find your business. Please sign in again.");
}
