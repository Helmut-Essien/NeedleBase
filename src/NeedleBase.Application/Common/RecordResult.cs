using NeedleBase.Application.Contracts.Errors;

namespace NeedleBase.Application.Common;

/// <summary>
/// Typed failure for customer, measurement and device order use cases (server services and device local services).
/// Callers switch on this, never on English text; the stable wire code is <see cref="RecordErrorMessages.Code"/>.
/// Device order results use it too (mapped from <c>OrderError</c> by <c>OrderErrorMapping.ToRecord</c>); the server
/// order service keeps <c>OrderResult</c>/<c>OrderError</c>.
/// </summary>
public enum RecordError
{
    None = 0,
    CustomerNotFound,
    MeasurementNotFound,
    CustomerConflict,
    MeasurementConflict,
    PhoneInvalid,
    Stale,
    Validation,
    StorageFull,
    OrderNotFound,
    OrderConflict,
    OrderLocked,
    OrderStatusInvalid,
    AssigneeInvalid,
    TotalMismatch
}

/// <summary>A failure without a value type; converts to any <see cref="RecordResult{T}"/>.</summary>
public readonly record struct RecordFailure(RecordError Error, string? Detail);

/// <summary>
/// Result of a customer/measurement use case: the value, or a typed <see cref="Error"/> with optional
/// user-safe <see cref="Detail"/> (e.g. the domain's validation sentence). Deconstructs to (value, message) for
/// display only; branch on <see cref="Error"/>.
/// </summary>
public readonly record struct RecordResult<T>(T? Value, RecordError Error, string? Detail = null)
{
    public bool IsSuccess => Error == RecordError.None && Value is not null;

    /// <summary>Stable code (e.g. <c>CUSTOMER_NOT_FOUND</c>), null on success.</summary>
    public string? Code => Error == RecordError.None ? null : RecordErrorMessages.Code(Error);

    /// <summary>User-facing sentence, null on success.</summary>
    public string? Message => Error == RecordError.None ? null : Detail ?? RecordErrorMessages.For(Error);

    public void Deconstruct(out T? value, out string? message)
    {
        value = Value;
        message = Message;
    }

    public static implicit operator RecordResult<T>(T? value) =>
        value is null ? new(default, RecordError.Validation) : new(value, RecordError.None);

    public static implicit operator RecordResult<T>(RecordFailure failure) => new(default, failure.Error, failure.Detail);
}

public static class RecordResult
{
    public static RecordFailure Fail(RecordError error, string? detail = null) => new(error, detail);
}
