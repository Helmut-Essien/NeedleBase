using NeedleBase.Application.Contracts.Orders;

namespace NeedleBase.Application.Orders;

/// <summary>Typed outcome of an order use case. Map <see cref="OrderError"/> to HTTP/UI; never match on text.</summary>
public enum OrderError
{
    None = 0,
    NotFound,
    CustomerNotFound,
    MeasurementNotFound,
    IdConflict,
    Locked,
    StatusInvalid,
    AssigneeInvalid,
    TotalMismatch,
    Invalid,
    /// <summary>BaseModifiedUtc no longer matches (someone else changed it). API: 409 STALE_UPDATE.</summary>
    Stale,
    /// <summary>Delete refused: money was recorded against the order. API: 409 ORDER_HAS_PAYMENTS.</summary>
    HasPayments,
    /// <summary>Edit would make the total less than what's already paid. API: 409 ORDER_TOTAL_BELOW_PAID.</summary>
    BelowPaid,
    /// <summary>Only Owners and Managers edit orders (tailors change status only). API: 403 ORDER_EDIT_FORBIDDEN.</summary>
    Forbidden
}

public sealed record OrderResult(OrderDetailDto? Order, OrderError Error, string? Detail = null)
{
    public bool IsSuccess => Error == OrderError.None && Order is not null;

    public static OrderResult Ok(OrderDetailDto? order) => new(order, order is null ? OrderError.NotFound : OrderError.None);

    public static OrderResult Fail(OrderError error, string? detail = null) => new(null, error, detail);
}
