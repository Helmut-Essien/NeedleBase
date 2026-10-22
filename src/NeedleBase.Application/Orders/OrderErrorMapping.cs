using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;

namespace NeedleBase.Application.Orders;

/// <summary>Maps the order use-case error to the shared <see cref="RecordError"/> and its stable code.</summary>
public static class OrderErrorMapping
{
    public static RecordError ToRecord(OrderError error) => error switch
    {
        OrderError.None => RecordError.None,
        OrderError.NotFound => RecordError.OrderNotFound,
        OrderError.CustomerNotFound => RecordError.CustomerNotFound,
        OrderError.MeasurementNotFound => RecordError.MeasurementNotFound,
        OrderError.IdConflict => RecordError.OrderConflict,
        OrderError.Locked => RecordError.OrderLocked,
        OrderError.StatusInvalid => RecordError.OrderStatusInvalid,
        OrderError.AssigneeInvalid => RecordError.AssigneeInvalid,
        OrderError.TotalMismatch => RecordError.TotalMismatch,
        OrderError.Stale => RecordError.Stale,
        OrderError.HasPayments => RecordError.Validation,
        _ => RecordError.Validation
    };

    /// <summary>Stable ProblemDetails/UI code for an order error (same values the API sends).</summary>
    public static string Code(OrderError error) =>
        error == OrderError.HasPayments ? OrderErrorMessages.HasPayments : RecordErrorMessages.Code(ToRecord(error));
}
