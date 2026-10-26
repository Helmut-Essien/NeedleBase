namespace NeedleBase.Application.Audit;

/// <summary>
/// Stable audit action codes (stored; clients filter on them). "entity.verb" for data changes, "auth.*"/"sync.*"/
/// "access.*" for security and activity events. Every new write path must map to one of these (see the
/// needlebase-architecture skill, "Audit trail").
/// </summary>
public static class AuditActions
{
    public const string CustomerCreated = "customer.created";
    public const string CustomerUpdated = "customer.updated";
    public const string CustomerDeleted = "customer.deleted";
    public const string CustomerRestored = "customer.restored";
    public const string MeasurementCreated = "measurement.created";
    public const string MeasurementUpdated = "measurement.updated";
    public const string MeasurementDeleted = "measurement.deleted";
    public const string MeasurementRestored = "measurement.restored";
    public const string OrderCreated = "order.created";
    public const string OrderUpdated = "order.updated";
    public const string OrderStatusChanged = "order.status_changed";
    public const string OrderAssigned = "order.assigned";
    public const string OrderLinesChanged = "order.lines_changed";
    public const string OrderDeleted = "order.deleted";
    public const string OrderRestored = "order.restored";
    public const string PaymentRecorded = "payment.recorded";
    public const string PaymentVoided = "payment.voided";
    public const string PaymentUpdated = "payment.updated";
    public const string PaymentDeleted = "payment.deleted";
    public const string BusinessCreated = "business.created";
    public const string BusinessUpdated = "business.updated";
    public const string StaffAdded = "staff.added";
    public const string StaffRoleChanged = "staff.role_changed";
    public const string StaffDeactivated = "staff.deactivated";
    public const string StaffReactivated = "staff.reactivated";
    public const string StaffRemoved = "staff.removed";
    public const string StaffInvited = "staff.invited";
    public const string StaffInviteRevoked = "staff.invite_revoked";
    public const string StaffOrdersReassigned = "staff.orders_reassigned";

    public const string LoginSucceeded = "auth.login";
    public const string LoginFailed = "auth.login_failed";
    public const string LoginThrottled = "auth.login_throttled";
    public const string LoginBlocked = "auth.login_blocked";
    public const string Logout = "auth.logout";
    public const string TokenReuse = "auth.token_reuse";
    public const string PasswordChanged = "auth.password_changed";
    public const string PasswordResetRequested = "auth.password_reset_requested";
    public const string PasswordReset = "auth.password_reset";
    public const string Registered = "auth.registered";

    public const string SyncPush = "sync.push";
    public const string SyncPull = "sync.pull";

    public const string PaymentsViewed = "access.payments_viewed";

    /// <summary>Security events (sign-in, tokens): Owner only in the activity log.</summary>
    public static bool IsSecurity(string action) => action.StartsWith("auth.", StringComparison.Ordinal);

    public static bool IsPayment(string action) =>
        action.StartsWith("payment.", StringComparison.Ordinal) || action == PaymentsViewed;

    public static string Display(string action) => action switch
    {
        CustomerCreated => "Customer added",
        CustomerUpdated => "Customer edited",
        CustomerDeleted => "Customer deleted",
        CustomerRestored => "Customer restored",
        MeasurementCreated => "Measurements added",
        MeasurementUpdated => "Measurements edited",
        MeasurementDeleted => "Measurements deleted",
        MeasurementRestored => "Measurements restored",
        OrderCreated => "Order created",
        OrderUpdated => "Order edited",
        OrderStatusChanged => "Status changed",
        OrderAssigned => "Order assigned",
        OrderLinesChanged => "Order items changed",
        OrderDeleted => "Order deleted",
        OrderRestored => "Order restored",
        PaymentRecorded => "Payment recorded",
        PaymentVoided => "Payment voided",
        PaymentUpdated => "Payment changed",
        PaymentDeleted => "Payment removed",
        BusinessCreated => "Business created",
        BusinessUpdated => "Business edited",
        StaffAdded => "Team member added",
        StaffRoleChanged => "Role changed",
        StaffDeactivated => "Team member deactivated",
        StaffReactivated => "Team member reactivated",
        StaffRemoved => "Team member removed",
        StaffInvited => "Invite sent",
        StaffInviteRevoked => "Invite cancelled",
        StaffOrdersReassigned => "Orders reassigned",
        LoginSucceeded => "Signed in",
        LoginFailed => "Sign-in failed",
        LoginThrottled => "Sign-in slowed down",
        LoginBlocked => "Sign-in blocked",
        Logout => "Signed out",
        TokenReuse => "Session revoked (token reuse)",
        PasswordChanged => "Password changed",
        PasswordResetRequested => "Password reset requested",
        PasswordReset => "Password reset",
        Registered => "Account registered",
        SyncPush => "Phone sent changes",
        SyncPull => "Phone synced",
        PaymentsViewed => "Viewed payments",
        _ => action
    };

    /// <summary>Plain label for a changed field ("DueDate" → "due date").</summary>
    public static string FieldLabel(string field) => field switch
    {
        "DueDate" => "due date",
        "FittingDate" => "fitting date",
        "TotalAmount" => "total",
        "UnitPrice" => "unit price",
        "AssignedToUserId" or "Assignee" => "assignee",
        "CustomerId" => "customer",
        "GarmentTemplate" => "garment",
        "MeasuredAtUtc" => "measured on",
        "RecordedAtUtc" => "payment date",
        "IsVoided" => "voided",
        "VoidReason" => "void reason",
        "PhotoRef" => "photo",
        "DisplayRef" => "order ref",
        "IsActive" => "active",
        "CurrencyCode" => "currency",
        "MeasurementUnit" => "unit",
        "fields" => "measurement fields",
        "items" => "items",
        _ => field.ToLowerInvariant()
    };

    /// <summary>Filter groups for the activity page (value → actions prefix).</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> Groups =
    [
        ("customer.", "Customers"),
        ("measurement.", "Measurements"),
        ("order.", "Orders"),
        ("payment.", "Payments"),
        ("staff.", "Team"),
        ("business.", "Business details"),
        ("auth.", "Sign-in and security"),
        ("sync.", "Phone sync"),
        ("access.", "Sensitive views")
    ];
}
