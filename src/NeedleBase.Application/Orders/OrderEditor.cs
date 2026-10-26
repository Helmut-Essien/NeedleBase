using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Payments;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Orders;

/// <summary>
/// Owner/Manager order edit (due date, notes, garment lines incl. price), shared by the API service, sync push and
/// the mobile local service so every path applies the same rules:
/// <list type="bullet">
/// <item>Only Owners and Managers (<see cref="CanEdit"/>); collected/cancelled orders are locked.</item>
/// <item>Existing lines keep their garment and frozen measurement snapshot; only quantity, price and notes change.</item>
/// <item>A new line snapshots the profile it names, or, with no profile and no values, the customer's latest
/// measurements for that garment (none = an honest "no measurements" line).</item>
/// <item>At least one line; the new total may not drop below what is already paid (<see cref="OrderError.BelowPaid"/>).</item>
/// </list>
/// Everything is validated before the order is touched, so a refused edit leaves the tracked entity unchanged.
/// </summary>
public static class OrderEditor
{
    public static bool CanEdit(string? role) => PaymentRules.CanManage(role);

    /// <summary>
    /// Tailors move work they are assigned to, or unassigned work (a solo helper picks it up); Owners and Managers
    /// move anything.
    /// </summary>
    public static bool CanChangeStatus(string? role, string? userId, string? assignedToUserId) =>
        role != NeedleBase.Domain.Enums.BusinessRole.Tailor ||
        string.IsNullOrEmpty(assignedToUserId) ||
        string.Equals(assignedToUserId, userId, StringComparison.Ordinal);

    /// <param name="order">Tracked order with its <see cref="Order.Lines"/> loaded.</param>
    /// <param name="customerProfiles">The order customer's measurement profiles (for new lines).</param>
    /// <param name="paid">Sum of non-voided payments on the order.</param>
    public static (OrderError Error, string? Detail) Apply(
        Order order,
        UpdateOrderRequest request,
        IReadOnlyList<MeasurementProfile> customerProfiles,
        decimal paid,
        DateTimeOffset now)
    {
        if (OrderStatus.IsTerminal(order.Status))
        {
            return (OrderError.Locked, null);
        }

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is { Length: > FieldLimits.Notes } ||
            request.AssignedToUserId?.Trim() is { Length: > FieldLimits.AssignedToUserId })
        {
            return (OrderError.Invalid, "header too long");
        }

        List<OrderLine>? newList = null;
        List<(OrderLine Line, CreateOrderLineRequest Req)> revisions = [];
        if (request.Lines is { } reqs)
        {
            if (reqs.Count == 0 || (!request.Lenient && reqs.Count > AmountRules.OrderLinesMax))
            {
                return (OrderError.Invalid, "line count");
            }

            var existing = order.Lines.ToDictionary(l => l.Id, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var adds = new List<(int Index, CreateOrderLineRequest Req)>();
            for (var i = 0; i < reqs.Count; i++)
            {
                var r = reqs[i];
                if (r is null)
                {
                    return (OrderError.Invalid, "null line");
                }

                if (!request.Lenient && AmountRules.CheckUnitPrice(r.UnitPrice) is { } priceError)
                {
                    return (OrderError.Invalid, priceError);
                }

                if (r.Quantity < 1 || r.Quantity > FieldLimits.OrderLineQuantityMax || r.UnitPrice < 0 ||
                    r.Notes?.Trim() is { Length: > FieldLimits.Notes })
                {
                    return (OrderError.Invalid, "line value");
                }

                var id = r.Id?.Trim();
                if (id is { Length: > 0 } && existing.ContainsKey(id))
                {
                    if (!seen.Add(id))
                    {
                        return (OrderError.Invalid, "duplicate line");
                    }

                    revisions.Add((existing[id], r));
                }
                else
                {
                    adds.Add((i, r));
                }
            }

            // New lines: resolve the snapshot source, then build (throws ArgumentException on bad garment/unit).
            var profilesById = customerProfiles.ToDictionary(p => p.Id, StringComparer.Ordinal);
            var addRequests = adds.Select(a => WithLatestProfile(a.Req, customerProfiles)).ToList();
            var (built, lineError) = OrderLineFactory.Build(order.BusinessId, order.Id, addRequests, profilesById, request.Lenient);
            if (built is null)
            {
                return (lineError, null);
            }

            // Final order as the user listed it.
            var slots = new OrderLine[reqs.Count];
            var revIndex = 0;
            var addIndex = 0;
            for (var i = 0; i < reqs.Count; i++)
            {
                slots[i] = addIndex < adds.Count && adds[addIndex].Index == i
                    ? built[addIndex++]
                    : revisions[revIndex++].Line;
            }

            newList = [.. slots];
            var newTotal = decimal.Round(
                revisions.Sum(x => decimal.Round(decimal.Round(x.Req.UnitPrice, 2, MidpointRounding.AwayFromZero) * x.Req.Quantity, 2, MidpointRounding.AwayFromZero)) +
                built.Sum(l => l.LineTotal), 2, MidpointRounding.AwayFromZero);
            if (OrderLineFactory.TotalMismatch(request.TotalAmount, newTotal))
            {
                return (OrderError.TotalMismatch, null);
            }

            if (newTotal < paid && newTotal < order.TotalAmount)
            {
                return (OrderError.BelowPaid, $"total {newTotal} < paid {paid}");
            }
        }

        // Validated: apply.
        order.UpdateHeader(request.DueDate, request.FittingDate, request.AssignedToUserId, notes, now);
        if (newList is not null)
        {
            foreach (var (line, r) in revisions)
            {
                line.Revise(r.Quantity, r.UnitPrice, r.Notes);
            }

            order.ReviseLines(newList, now);
        }

        return (OrderError.None, null);
    }

    /// <summary>True when the request's lines differ from the order's (ids, quantity, price, notes).</summary>
    public static bool LinesDiffer(Order order, IReadOnlyList<CreateOrderLineRequest> lines)
    {
        var current = order.Lines.OrderBy(l => l.SortOrder).ToList();
        if (current.Count != lines.Count)
        {
            return true;
        }

        for (var i = 0; i < current.Count; i++)
        {
            var a = current[i];
            var b = lines[i];
            if (!string.Equals(a.Id, b.Id?.Trim(), StringComparison.Ordinal) || a.Quantity != b.Quantity ||
                a.UnitPrice != decimal.Round(b.UnitPrice, 2, MidpointRounding.AwayFromZero) ||
                !string.Equals(a.Notes, string.IsNullOrWhiteSpace(b.Notes) ? null : b.Notes.Trim(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Edit rows for the order's current lines (what an edit form starts from).</summary>
    public static List<CreateOrderLineRequest> RequestsFor(IEnumerable<OrderLineDto> lines) =>
        lines.OrderBy(l => l.SortOrder).Select(l => new CreateOrderLineRequest
        {
            Id = l.Id,
            GarmentTemplate = l.GarmentTemplate,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            MeasurementProfileId = l.MeasurementProfileId,
            Unit = l.Unit,
            Notes = l.Notes
        }).ToList();

    /// <summary>
    /// Which of the customer's profiles an edit can use: ids named by new lines, and garments of new lines that
    /// carry neither a profile nor values (they take the latest profile). Empty = no profile query needed.
    /// </summary>
    public static (List<string> Ids, List<string> Garments) ProfileNeeds(Order order, IReadOnlyList<CreateOrderLineRequest> lines)
    {
        var existing = order.Lines.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var garments = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in lines)
        {
            if (r is null || (r.Id?.Trim() is { Length: > 0 } id && existing.Contains(id)))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(r.MeasurementProfileId))
            {
                ids.Add(r.MeasurementProfileId.Trim());
            }
            else if (r.MeasurementFields is not { Count: > 0 } && !string.IsNullOrWhiteSpace(r.GarmentTemplate))
            {
                garments.Add(r.GarmentTemplate.Trim().ToLowerInvariant());
            }
        }

        return ([.. ids], [.. garments]);
    }

    private static CreateOrderLineRequest WithLatestProfile(CreateOrderLineRequest r, IReadOnlyList<MeasurementProfile> profiles)
    {
        if (!string.IsNullOrWhiteSpace(r.MeasurementProfileId) || r.MeasurementFields is { Count: > 0 })
        {
            return r;
        }

        var garment = r.GarmentTemplate?.Trim().ToLowerInvariant();
        var latest = profiles
            .Where(p => string.Equals(p.GarmentTemplate, garment, StringComparison.Ordinal))
            .OrderByDescending(p => p.MeasuredAtUtc)
            .ThenByDescending(p => p.DateModifiedUtc)
            .FirstOrDefault();
        if (latest is null)
        {
            return r;
        }

        return new CreateOrderLineRequest
        {
            GarmentTemplate = r.GarmentTemplate ?? string.Empty,
            Quantity = r.Quantity,
            UnitPrice = r.UnitPrice,
            MeasurementProfileId = latest.Id,
            Unit = r.Unit,
            Notes = r.Notes
        };
    }
}
