namespace NeedleBase.Application.Abstractions;

/// <summary>
/// Queues paid-plan staff emails when an order is assigned (Workshop+ / unlimited).
/// No-ops when email is off, the plan does not include assignment emails, or the shop is closed / read-only.
/// </summary>
public interface IPlanEmailNotifier
{
    /// <summary>
    /// Email the new assignee when <paramref name="assigneeUserId"/> is set and differs from
    /// <paramref name="previousAssigneeUserId"/>. Safe to call after a successful save.
    /// </summary>
    Task NotifyAssignmentAsync(
        string businessId,
        string orderId,
        string displayRef,
        string customerName,
        DateOnly? dueDate,
        string? previousAssigneeUserId,
        string? assigneeUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One digest email for a bulk reassignment (avoids flooding the SMTP queue with one mail per order).
    /// When <paramref name="orders"/> has a single row, behaves like <see cref="NotifyAssignmentAsync"/>.
    /// </summary>
    Task NotifyBulkAssignmentAsync(
        string businessId,
        string? previousAssigneeUserId,
        string assigneeUserId,
        IReadOnlyList<PlanEmailAssignedOrder> orders,
        CancellationToken cancellationToken = default);
}

/// <summary>One order line in a bulk assignment digest.</summary>
public sealed record PlanEmailAssignedOrder(string OrderId, string DisplayRef, DateOnly? DueDate, string CustomerName);
