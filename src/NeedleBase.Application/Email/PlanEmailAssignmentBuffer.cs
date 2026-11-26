using NeedleBase.Application.Abstractions;

namespace NeedleBase.Application.Email;

/// <summary>
/// Holds assignment-email intents until the surrounding DB transaction commits.
/// Sync push (and API updates under <c>InTransactionAsync</c>) can roll back after
/// <see cref="Orders.OrderService"/> SaveChanges; the in-memory SMTP queue cannot.
/// </summary>
public sealed class PlanEmailAssignmentBuffer
{
    private readonly List<Pending> _pending = [];

    public void Enqueue(
        string businessId,
        string orderId,
        string displayRef,
        string customerName,
        DateOnly? dueDate,
        string? previousAssigneeUserId,
        string? assigneeUserId)
    {
        _pending.Add(new Pending(
            businessId, orderId, displayRef, customerName, dueDate, previousAssigneeUserId, assigneeUserId));
    }

    public void Clear() => _pending.Clear();

    public async Task FlushAsync(IPlanEmailNotifier notifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        if (_pending.Count == 0)
        {
            return;
        }

        var batch = _pending.ToList();
        _pending.Clear();
        foreach (var item in batch)
        {
            await notifier.NotifyAssignmentAsync(
                item.BusinessId, item.OrderId, item.DisplayRef, item.CustomerName, item.DueDate,
                item.PreviousAssigneeUserId, item.AssigneeUserId, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record Pending(
        string BusinessId,
        string OrderId,
        string DisplayRef,
        string CustomerName,
        DateOnly? DueDate,
        string? PreviousAssigneeUserId,
        string? AssigneeUserId);
}
