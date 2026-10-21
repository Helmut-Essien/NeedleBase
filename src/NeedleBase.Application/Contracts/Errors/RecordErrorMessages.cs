using NeedleBase.Application.Common;

namespace NeedleBase.Application.Contracts.Errors;

/// <summary>Stable codes and default copy for <see cref="RecordError"/> (API ProblemDetails <c>code</c>, device messages).</summary>
public static class RecordErrorMessages
{
    public const string CustomerNotFound = "CUSTOMER_NOT_FOUND";
    public const string MeasurementNotFound = "MEASUREMENT_NOT_FOUND";
    public const string CustomerConflict = "CUSTOMER_CONFLICT";
    public const string MeasurementConflict = "MEASUREMENT_CONFLICT";
    public const string PhoneInvalid = "PHONE_INVALID";
    public const string Validation = "VALIDATION_FAILED";
    public const string StorageFull = "STORAGE_FULL";

    public static string Code(RecordError error) => error switch
    {
        RecordError.CustomerNotFound => CustomerNotFound,
        RecordError.MeasurementNotFound => MeasurementNotFound,
        RecordError.CustomerConflict => CustomerConflict,
        RecordError.MeasurementConflict => MeasurementConflict,
        RecordError.PhoneInvalid => PhoneInvalid,
        RecordError.Stale => StaleUpdate.Code,
        RecordError.StorageFull => StorageFull,
        RecordError.OrderNotFound => OrderErrorMessages.OrderNotFound,
        RecordError.OrderConflict => OrderErrorMessages.OrderConflict,
        RecordError.OrderLocked => OrderErrorMessages.OrderLocked,
        RecordError.OrderStatusInvalid => OrderErrorMessages.OrderStatusInvalid,
        RecordError.AssigneeInvalid => OrderErrorMessages.AssigneeInvalid,
        RecordError.TotalMismatch => OrderErrorMessages.TotalMismatch,
        _ => Validation
    };

    public static string For(RecordError error) => error switch
    {
        RecordError.CustomerNotFound => "Customer not found.",
        RecordError.MeasurementNotFound => "Measurement not found.",
        RecordError.CustomerConflict => "This customer has already been saved.",
        RecordError.MeasurementConflict => "This measurement has already been saved.",
        RecordError.PhoneInvalid => NeedleBase.Domain.Common.PhoneNumbers.FormatMessage,
        RecordError.Stale => StaleUpdate.Message,
        RecordError.StorageFull => "This device is full. Free some space, then try again.",
        RecordError.OrderNotFound or RecordError.OrderConflict or RecordError.OrderLocked
            or RecordError.OrderStatusInvalid or RecordError.AssigneeInvalid or RecordError.TotalMismatch
            => OrderErrorMessages.For(Code(error)),
        _ => "Check the details and try again."
    };
}
