namespace NeedleBase.Application.Contracts.Errors;

/// <summary>Friendly copy for order ProblemDetails codes (shared by API detail and mobile/web UI).</summary>
public static class OrderErrorMessages
{
    public const string OrderNotFound = "ORDER_NOT_FOUND";
    public const string OrderConflict = "ORDER_CONFLICT";
    public const string OrderLocked = "ORDER_LOCKED";
    public const string OrderStatusInvalid = "ORDER_STATUS_INVALID";
    public const string AssigneeInvalid = "ASSIGNEE_INVALID";
    public const string TotalMismatch = "ORDER_TOTAL_MISMATCH";
    public const string Validation = "VALIDATION_FAILED";
    public const string CustomerNotFound = "CUSTOMER_NOT_FOUND";
    public const string MeasurementNotFound = "MEASUREMENT_NOT_FOUND";
    public const string HasPayments = "ORDER_HAS_PAYMENTS";
    public const string TotalBelowPaid = "ORDER_TOTAL_BELOW_PAID";
    public const string EditForbidden = "ORDER_EDIT_FORBIDDEN";
    public const string NotAssigned = "ORDER_NOT_ASSIGNED";

    public const string Fallback = "Couldn't save the order. Check the details and try again.";

    public static string For(string? code) => code switch
    {
        OrderNotFound => "We couldn't find that order. It may have been deleted.",
        OrderConflict => "This order has already been saved.",
        OrderLocked => "This order is collected or cancelled, so it can't be changed.",
        OrderStatusInvalid => "That status isn't available. Pick one from the list.",
        AssigneeInvalid => "That person isn't on your team. Pick someone else.",
        TotalMismatch => "The total doesn't match the garments. Check prices and quantities.",
        CustomerNotFound => "We couldn't find that customer.",
        MeasurementNotFound => "We couldn't find those measurements for this customer.",
        Validation => "Check the order details and try again.",
        HasPayments => "Payments are recorded on this order, so it can't be deleted. Cancel it instead.",
        TotalBelowPaid => "The new total would be less than the customer has already paid. Keep the total at or above the amount paid, or void a payment first.",
        EditForbidden => "Only the owner or a manager can edit an order. You can still change its status.",
        NotAssigned => "This order is assigned to someone else. Ask the owner or a manager to change it.",
        NeedleBase.Application.Common.StaleUpdate.Code => "Someone else saved a newer version. Reload to see it, then make your change again.",
        _ => Fallback
    };
}
