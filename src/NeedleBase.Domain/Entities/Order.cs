using NeedleBase.Domain.Common;
using NeedleBase.Domain.Enums;
using NeedleBase.Domain.Exceptions;

namespace NeedleBase.Domain.Entities;

/// <summary>Sewing job for a customer within a Business workspace.</summary>
public class Order : Entity, IBusinessScoped
{
    public string BusinessId { get; private set; } = string.Empty;
    public string CustomerId { get; private set; } = string.Empty;

    /// <summary>
    /// Short readable ref (8 chars, upper case) for lists and receipts. Defaults to the last 8
    /// characters of the ULID id; unique per business (callers retry with <see cref="NewRandomDisplayRef"/>
    /// on a collision).
    /// </summary>
    public string DisplayRef { get; private set; } = string.Empty;

    public string Status { get; private set; } = OrderStatus.New;
    public DateOnly? DueDate { get; private set; }
    public DateOnly? FittingDate { get; private set; }
    public string? AssignedToUserId { get; private set; }
    /// <summary>Always the sum of line totals (qty × unit price). Discounts are planned, not built.</summary>
    public decimal TotalAmount { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset DateModifiedUtc { get; private set; }

    /// <summary>Garment lines (EF navigation; mutated only via domain methods).</summary>
    public ICollection<OrderLine> Lines { get; private set; } = new List<OrderLine>();

    private Order()
    {
    }

    public static Order Create(
        string id,
        string businessId,
        string customerId,
        DateOnly? dueDate,
        DateOnly? fittingDate,
        string? assignedToUserId,
        string? notes,
        IEnumerable<OrderLine> lines,
        DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        var order = new Order
        {
            Id = id,
            BusinessId = businessId,
            CustomerId = customerId,
            DisplayRef = BuildDisplayRef(id),
            Status = OrderStatus.New,
            CreatedAtUtc = utcNow,
            DateModifiedUtc = utcNow
        };
        order.ApplyHeader(dueDate, fittingDate, assignedToUserId, notes, utcNow);
        order.ReplaceLines(lines);
        return order;
    }

    public void UpdateHeader(
        DateOnly? dueDate,
        DateOnly? fittingDate,
        string? assignedToUserId,
        string? notes,
        DateTimeOffset utcNow)
    {
        if (OrderStatus.IsTerminal(Status))
        {
            throw new DomainRuleException(
                DomainErrorCodes.OrderLocked,
                $"Cannot edit a {Status.ToLowerInvariant()} order.");
        }

        ApplyHeader(dueDate, fittingDate, assignedToUserId, notes, utcNow);
    }

    public void SetStatus(string status, DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        status = status.Trim();
        if (!OrderStatus.IsValid(status))
        {
            throw new DomainRuleException(DomainErrorCodes.OrderStatusInvalid, $"Unknown order status: {status}");
        }

        if (OrderStatus.IsTerminal(Status) && status != Status)
        {
            throw new DomainRuleException(
                DomainErrorCodes.OrderLocked,
                $"Cannot change status of a {Status.ToLowerInvariant()} order.");
        }

        Status = status;
        DateModifiedUtc = utcNow;
    }

    public bool IsOverdue(DateOnly today) => OrderStatus.IsOverdue(Status, DueDate, today);

    public static string BuildDisplayRef(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var trimmed = id.Trim();
        var take = Math.Min(FieldLimits.OrderDisplayRef, trimmed.Length);
        return trimmed[^take..].ToUpperInvariant();
    }

    private const string RefAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford base32

    /// <summary>Random 8-char ref used only after a <see cref="BuildDisplayRef"/> collision.</summary>
    public static string NewRandomDisplayRef()
    {
        Span<char> chars = stackalloc char[FieldLimits.OrderDisplayRef];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = RefAlphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(RefAlphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>Replaces the ref after a per-business uniqueness collision (before first save).</summary>
    public void ReassignDisplayRef(string displayRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayRef);
        displayRef = displayRef.Trim().ToUpperInvariant();
        if (displayRef.Length > FieldLimits.OrderDisplayRef)
        {
            throw new ArgumentException("Display ref is too long.", nameof(displayRef));
        }

        DisplayRef = displayRef;
    }

    private void ApplyHeader(
        DateOnly? dueDate,
        DateOnly? fittingDate,
        string? assignedToUserId,
        string? notes,
        DateTimeOffset utcNow)
    {
        assignedToUserId = string.IsNullOrWhiteSpace(assignedToUserId) ? null : assignedToUserId.Trim();
        if (assignedToUserId is { Length: > FieldLimits.AssignedToUserId })
        {
            throw new ArgumentException(
                $"Assignee id must be at most {FieldLimits.AssignedToUserId} characters.",
                nameof(assignedToUserId));
        }

        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (notes is { Length: > FieldLimits.Notes })
        {
            throw new ArgumentException($"Notes must be at most {FieldLimits.Notes} characters.", nameof(notes));
        }

        DueDate = dueDate;
        FittingDate = fittingDate;
        AssignedToUserId = assignedToUserId;
        Notes = notes;
        DateModifiedUtc = utcNow;
    }

    private void ReplaceLines(IEnumerable<OrderLine> lines)
    {
        var list = lines?.ToList() ?? [];
        if (list.Count == 0)
        {
            throw new ArgumentException("An order needs at least one garment line.", nameof(lines));
        }

        Lines.Clear();
        var sort = 0;
        foreach (var line in list)
        {
            if (!string.Equals(line.BusinessId, BusinessId, StringComparison.Ordinal) ||
                !string.Equals(line.OrderId, Id, StringComparison.Ordinal))
            {
                throw new ArgumentException("Order line business/order id mismatch.", nameof(lines));
            }

            line.SetSortOrder(sort++);
            Lines.Add(line);
        }

        TotalAmount = decimal.Round(Lines.Sum(l => l.LineTotal), 2, MidpointRounding.AwayFromZero);
    }
}
