namespace NeedleBase.Domain.Enums;

/// <summary>Production status for an order (plain string constants for storage + UI).</summary>
public static class OrderStatus
{
    public const string New = "New";
    public const string Cutting = "Cutting";
    public const string Sewing = "Sewing";
    public const string Fitting = "Fitting";
    public const string Ready = "Ready";
    public const string Collected = "Collected";
    public const string Cancelled = "Cancelled";

    /// <summary>Main flow in production order (excludes Cancelled).</summary>
    public static readonly IReadOnlyList<string> ProductionFlow =
    [
        New, Cutting, Sewing, Fitting, Ready, Collected
    ];

    public static readonly IReadOnlyList<string> All =
    [
        New, Cutting, Sewing, Fitting, Ready, Collected, Cancelled
    ];

    /// <summary>Open work (everything not Collected/Cancelled): Home's "Active orders" count and the Orders "Active" filter.</summary>
    public static readonly IReadOnlyList<string> Active =
    [
        New, Cutting, Sewing, Fitting, Ready
    ];

    /// <summary>List-filter key for <see cref="Active"/> (not a status; never stored on an order).</summary>
    public const string ActiveFilter = "Active";

    public static bool IsValid(string? status) =>
        status is New or Cutting or Sewing or Fitting or Ready or Collected or Cancelled;

    public static bool IsTerminal(string? status) =>
        status is Collected or Cancelled;

    /// <summary>UI label — Ready displays as "Ready to collect".</summary>
    public static string DisplayName(string status) => status switch
    {
        Ready => "Ready to collect",
        _ => status
    };

    /// <summary>Derived overdue badge: due date passed and not Collected/Cancelled.</summary>
    public static bool IsOverdue(string status, DateOnly? dueDate, DateOnly today) =>
        dueDate is { } due && due < today && !IsTerminal(status);
}
