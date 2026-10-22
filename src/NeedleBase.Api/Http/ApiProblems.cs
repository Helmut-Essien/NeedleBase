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
        Create(ctx, StatusCodes.Status404NotFound, RecordErrorMessages.CustomerNotFound, "Customer not found",
            "We couldn't find that customer. It may have been deleted.");

    public static ObjectResult MeasurementNotFound(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status404NotFound, RecordErrorMessages.MeasurementNotFound, "Measurement not found",
            "We couldn't find that measurement. It may have been deleted.");

    public static ObjectResult CustomerConflict(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status409Conflict, RecordErrorMessages.CustomerConflict, "Customer already exists",
            "This customer has already been saved.");

    public static ObjectResult MeasurementConflict(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status409Conflict, RecordErrorMessages.MeasurementConflict, "Measurement already exists",
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
            OrderError.Stale => (StatusCodes.Status409Conflict, NeedleBase.Application.Common.StaleUpdate.Code, "Changed by someone else"),
            OrderError.HasPayments => (StatusCodes.Status409Conflict, OrderErrorMessages.HasPayments, "Order has payments"),
            _ => (StatusCodes.Status400BadRequest, OrderErrorMessages.Validation, "Check the order details")
        };

        return Create(ctx, status, code, title, OrderErrorMessages.For(code));
    }

    /// <summary>
    /// Single mapping from <see cref="NeedleBase.Application.Common.RecordError"/> (customers, measurements) to the
    /// same status + code + copy these endpoints always returned. Validation keeps the per-entity
    /// <paramref name="saveFailedCode"/> (CUSTOMER_SAVE_FAILED / MEASUREMENT_SAVE_FAILED) for older clients.
    /// </summary>
    public static ObjectResult ForRecord(
        HttpContext ctx, NeedleBase.Application.Common.RecordError error, string saveFailedCode, string what) => error switch
    {
        NeedleBase.Application.Common.RecordError.CustomerNotFound => CustomerNotFound(ctx),
        NeedleBase.Application.Common.RecordError.MeasurementNotFound => MeasurementNotFound(ctx),
        NeedleBase.Application.Common.RecordError.CustomerConflict => CustomerConflict(ctx),
        NeedleBase.Application.Common.RecordError.MeasurementConflict => MeasurementConflict(ctx),
        NeedleBase.Application.Common.RecordError.PhoneInvalid => PhoneInvalid(ctx),
        NeedleBase.Application.Common.RecordError.Stale => StaleUpdate(ctx),
        _ => SaveFailed(ctx, saveFailedCode, what)
    };

    /// <summary>Payment errors: status + stable code + the shared friendly copy.</summary>
    public static ObjectResult ForPayment(HttpContext ctx, NeedleBase.Application.Common.RecordError error, string? detail = null)
    {
        var status = error switch
        {
            NeedleBase.Application.Common.RecordError.PaymentNotFound or NeedleBase.Application.Common.RecordError.OrderNotFound => StatusCodes.Status404NotFound,
            NeedleBase.Application.Common.RecordError.PaymentConflict or NeedleBase.Application.Common.RecordError.PaymentOrderLocked
                or NeedleBase.Application.Common.RecordError.PaymentExceedsBalance
                or NeedleBase.Application.Common.RecordError.Stale => StatusCodes.Status409Conflict,
            NeedleBase.Application.Common.RecordError.PaymentForbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };
        var code = RecordErrorMessages.Code(error);
        return Create(ctx, status, code, "Couldn't save the payment",
            string.IsNullOrWhiteSpace(detail) ? RecordErrorMessages.For(error) : detail);
    }

    public static ObjectResult StaleUpdate(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status409Conflict, NeedleBase.Application.Common.StaleUpdate.Code, "Changed by someone else",
            "Someone else saved a newer version. Reload to see it, then make your change again.");

    public static ObjectResult PhoneInvalid(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status400BadRequest, RecordErrorMessages.PhoneInvalid, "Phone number not valid",
            NeedleBase.Domain.Common.PhoneNumbers.FormatMessage);

    public static ObjectResult OrderNotFound(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status404NotFound, "ORDER_NOT_FOUND", "Order not found",
            "We couldn't find that order. It may have been deleted.");

    public static ObjectResult OrderConflict(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status409Conflict, "ORDER_CONFLICT", "Order already exists",
            "This order has already been saved.");

    public static ObjectResult SaveFailed(HttpContext ctx, string code, string what) =>
        Create(ctx, StatusCodes.Status400BadRequest, code, $"Couldn't save {what}",
            $"We couldn't save the {what}. Check the details and try again.");

    /// <summary>
    /// 400 with <c>SYNC_CURSOR_INVALID</c> or <c>SYNC_EPOCH_CHANGED</c>. Both carry <c>reset: SYNC_CURSOR_INVALID</c>
    /// so app builds that only know the older code still start over.
    /// </summary>
    public static ObjectResult SyncCursorInvalid(HttpContext ctx, string? code = null)
    {
        var result = Create(ctx, StatusCodes.Status400BadRequest, code ?? NeedleBase.Application.Contracts.Sync.SyncErrorCodes.CursorInvalid,
            "Sync needs to start again", "Your device will download your data again. Please try syncing once more.");
        if (result.Value is ProblemDetails problem)
        {
            problem.Extensions["reset"] = NeedleBase.Application.Contracts.Sync.SyncErrorCodes.CursorInvalid;
        }

        return result;
    }

    public static ObjectResult SyncMaintenance(HttpContext ctx)
    {
        ctx.Response.Headers.RetryAfter = ((int)NeedleBase.Application.Contracts.Sync.SyncMaintenanceException.RetryAfter.TotalSeconds)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Create(ctx, StatusCodes.Status503ServiceUnavailable, NeedleBase.Application.Contracts.Sync.SyncErrorCodes.Maintenance,
            "Sync is paused for a moment", "NeedleBase is updating its data. Your changes are kept and will sync shortly.");
    }

    public static ObjectResult SessionInvalid(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status401Unauthorized, "SESSION_INVALID", "Please sign in again",
            "Your session is no longer valid. Please sign in again.");

    public static ObjectResult BusinessNotFound(HttpContext ctx) =>
        Create(ctx, StatusCodes.Status404NotFound, "BUSINESS_MISSING", "Business not found",
            "We couldn't find your business. Please sign in again.");
}
