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
    Invalid
}

public sealed record OrderResult(OrderDetailDto? Order, OrderError Error, string? Detail = null)
{
    public bool IsSuccess => Error == OrderError.None && Order is not null;

    public static OrderResult Ok(OrderDetailDto? order) => new(order, order is null ? OrderError.NotFound : OrderError.None);

    public static OrderResult Fail(OrderError error, string? detail = null) => new(null, error, detail);
}
