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
    public const string PaymentNotFound = "PAYMENT_NOT_FOUND";
    public const string PaymentConflict = "PAYMENT_CONFLICT";
    public const string PaymentInvalid = "PAYMENT_INVALID";
    public const string PaymentExceedsBalance = "PAYMENT_EXCEEDS_BALANCE";
    public const string PaymentOrderLocked = "PAYMENT_ORDER_LOCKED";
    public const string PaymentForbidden = "PAYMENT_FORBIDDEN";

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
        RecordError.PaymentNotFound => PaymentNotFound,
        RecordError.PaymentConflict => PaymentConflict,
        RecordError.PaymentInvalid => PaymentInvalid,
        RecordError.PaymentExceedsBalance => PaymentExceedsBalance,
        RecordError.PaymentOrderLocked => PaymentOrderLocked,
        RecordError.PaymentForbidden => PaymentForbidden,
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
        RecordError.PaymentNotFound => "We couldn't find that payment.",
        RecordError.PaymentConflict => "This payment has already been saved.",
        RecordError.PaymentInvalid => "Check the amount and how the customer paid.",
        RecordError.PaymentExceedsBalance => "That's more than what's left to pay on this order.",
        RecordError.PaymentOrderLocked => "This order is cancelled, so no payments can be recorded.",
        RecordError.PaymentForbidden => "Only the owner or a manager can record payments.",
        _ => "Check the details and try again."
    };
}
