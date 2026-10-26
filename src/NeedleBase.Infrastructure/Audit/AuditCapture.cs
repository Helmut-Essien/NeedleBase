using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Infrastructure.Audit;

/// <summary>
/// Turns the change tracker into audit rows just before SaveChanges, so every writer (REST, web, sync push and its
/// conflict resolutions, seeding) is covered without per-service code and the rows commit or roll back with the
/// change. Sensitive values are masked: phones keep the last 4 digits, notes/photo refs show only "(hidden)",
/// measurement values are never copied (only which fields changed), secrets never reach audited entities.
/// </summary>
internal static class AuditCapture
{
    public sealed record Change(string Field, string? Old, string? New);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal)
    {
        nameof(Entity.Id), "BusinessId", "CreatedAtUtc", "DateModifiedUtc", "VoidedAtUtc", "JoinedAtUtc",
        nameof(OrderLine.MeasurementFieldsJson), nameof(OrderLine.SortOrder),
        nameof(OrderLine.MeasurementTakenAtUtc)
    };

    private static readonly HashSet<string> HiddenText = new(StringComparer.Ordinal)
    {
        "Notes", "Note", "PhotoRef"
    };

    public static List<AuditEvent> Capture(IEnumerable<EntityEntry> entries, AuditContext? ctx, ISet<string> restored, DateTimeOffset now)
    {
        var events = new List<AuditEvent>();
        var list = entries.Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        var lineCounts = list.Where(e => e.Entity is OrderLine)
            .GroupBy(e => ((OrderLine)e.Entity).OrderId)
            .ToDictionary(g => g.Key, g => (
                Added: g.Count(e => e.State == EntityState.Added),
                Changed: g.Count(e => e.State == EntityState.Modified && Diff(e).Count > 0),
                Removed: g.Count(e => e.State == EntityState.Deleted)));
        var ordersInSave = new HashSet<string>(list.Where(e => e.Entity is Order).Select(e => ((Order)e.Entity).Id));

        // Anonymous saves (registration): the new member who is being created is the actor of the whole save.
        var registrant = ctx?.UserId is null
            ? list.Where(e => e.State == EntityState.Added).Select(e => e.Entity).OfType<StaffMembership>().FirstOrDefault()
            : null;

        foreach (var entry in list)
        {
            try
            {
                switch (entry.Entity)
                {
                    case Customer c: Customer(entry, c); break;
                    case MeasurementProfile m: Measurement(entry, m); break;
                    case Order o: OrderEvents(entry, o); break;
                    case Payment p: PaymentEvents(entry, p); break;
                    case StaffMembership s: Staff(entry, s); break;
                    case Business b: BusinessEvents(entry, b); break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never lose the fact of a change because a detail couldn't be described.
                var type = entry.Entity.GetType().Name.ToLowerInvariant();
                Add(BusinessOf(entry.Entity), $"{type}.changed", $"Changed {type} (details unavailable)", type, (entry.Entity as Entity)?.Id);
            }
        }

        foreach (var (orderId, counts) in lineCounts.Where(kv => !ordersInSave.Contains(kv.Key)))
        {
            if (counts.Added + counts.Changed + counts.Removed == 0)
            {
                continue;
            }

            var business = list.Select(e => e.Entity).OfType<OrderLine>().First(l => l.OrderId == orderId).BusinessId;
            Add(business, AuditActions.OrderLinesChanged, "Changed order items: " + LineText(counts), "order", orderId, orderId: orderId,
                changes: [new Change("items", null, LineText(counts)), .. LineDetails(orderId)]);
        }

        return events;

        void Customer(EntityEntry e, Customer c)
        {
            switch (e.State)
            {
                case EntityState.Added:
                    var restoredC = restored.Contains(SyncEntityType.Customer + ":" + c.Id);
                    Add(c.BusinessId, restoredC ? AuditActions.CustomerRestored : AuditActions.CustomerCreated,
                        $"{(restoredC ? "Restored" : "Added")} customer {AuditMask.Short(c.Name, 60)}", "customer", c.Id, customerId: c.Id,
                        changes: Snapshot(e));
                    break;
                case EntityState.Modified:
                    var d = Diff(e);
                    if (d.Count > 0)
                    {
                        Add(c.BusinessId, AuditActions.CustomerUpdated, $"Edited customer {AuditMask.Short(c.Name, 60)}: {Fields(d)}", "customer", c.Id,
                            customerId: c.Id, changes: d);
                    }

                    break;
                case EntityState.Deleted:
                    Add(c.BusinessId, AuditActions.CustomerDeleted, $"Deleted customer {AuditMask.Short(c.Name, 60)} and their measurements",
                        "customer", c.Id, customerId: c.Id);
                    break;
            }
        }

        void Measurement(EntityEntry e, MeasurementProfile m)
        {
            var label = $"{m.GarmentTemplate} measurements";
            switch (e.State)
            {
                case EntityState.Added:
                    var restoredM = restored.Contains(SyncEntityType.Measurement + ":" + m.Id);
                    Add(m.BusinessId, restoredM ? AuditActions.MeasurementRestored : AuditActions.MeasurementCreated,
                        $"{(restoredM ? "Restored" : "Added")} {label}", "measurement", m.Id, customerId: m.CustomerId,
                        changes: [new Change("fields", null, FieldNames(null, m.FieldsJson))]);
                    break;
                case EntityState.Modified:
                    var d = Diff(e);
                    if (d.Count > 0)
                    {
                        Add(m.BusinessId, AuditActions.MeasurementUpdated, $"Edited {label}: {Fields(d)}", "measurement", m.Id,
                            customerId: m.CustomerId, changes: d);
                    }

                    break;
                case EntityState.Deleted:
                    Add(m.BusinessId, AuditActions.MeasurementDeleted, $"Deleted {label}", "measurement", m.Id, customerId: m.CustomerId);
                    break;
            }
        }

        void OrderEvents(EntityEntry e, Order o)
        {
            var reference = "#" + o.DisplayRef;
            lineCounts.TryGetValue(o.Id, out var lines);
            var lineText = LineText(lines);
            switch (e.State)
            {
                case EntityState.Added:
                    var restoredO = restored.Contains(SyncEntityType.Order + ":" + o.Id);
                    Add(o.BusinessId, restoredO ? AuditActions.OrderRestored : AuditActions.OrderCreated,
                        $"{(restoredO ? "Restored" : "Created")} order {reference} ({Money.Format(o.TotalAmount)}, {lines.Added} item{(lines.Added == 1 ? "" : "s")})",
                        "order", o.Id, orderId: o.Id, customerId: o.CustomerId, changes: Snapshot(e));
                    break;
                case EntityState.Modified:
                    var d = Diff(e);
                    var status = d.FirstOrDefault(c => c.Field == nameof(Order.Status));
                    var assignee = d.FirstOrDefault(c => c.Field == nameof(Order.AssignedToUserId));
                    if (status is not null)
                    {
                        Add(o.BusinessId, AuditActions.OrderStatusChanged,
                            $"Order {reference}: {OrderStatus.DisplayName(status.Old ?? "")} → {OrderStatus.DisplayName(status.New ?? "")}",
                            "order", o.Id, orderId: o.Id, customerId: o.CustomerId, changes: [status]);
                    }

                    if (assignee is not null)
                    {
                        Add(o.BusinessId, AuditActions.OrderAssigned,
                            assignee.New is null ? $"Order {reference} unassigned" : $"Order {reference} assigned",
                            "order", o.Id, orderId: o.Id, customerId: o.CustomerId, changes: [assignee]);
                    }

                    var rest = d.Where(c => c != status && c != assignee).ToList();
                    if (lines.Added + lines.Changed + lines.Removed > 0)
                    {
                        rest.Add(new Change("items", null, lineText));
                    }

                    if (rest.Count > 0)
                    {
                        // Summary names the fields; the change list also says which garment changed and how.
                        Add(o.BusinessId, AuditActions.OrderUpdated, $"Edited order {reference}: {Fields(rest)}", "order", o.Id,
                            orderId: o.Id, customerId: o.CustomerId, changes: [.. rest, .. LineDetails(o.Id)]);
                    }

                    break;
                case EntityState.Deleted:
                    Add(o.BusinessId, AuditActions.OrderDeleted, $"Deleted order {reference}", "order", o.Id, orderId: o.Id, customerId: o.CustomerId);
                    break;
            }
        }

        void PaymentEvents(EntityEntry e, Payment p)
        {
            var amount = $"{Money.Format(p.Amount)} ({PaymentMethod.DisplayName(p.Method)})";
            switch (e.State)
            {
                case EntityState.Added:
                    Add(p.BusinessId, AuditActions.PaymentRecorded, $"Recorded payment {amount}", "payment", p.Id,
                        orderId: p.OrderId, customerId: p.CustomerId, changes: Snapshot(e));
                    break;
                case EntityState.Modified:
                    var d = Diff(e);
                    if (d.Any(c => c.Field == nameof(Payment.IsVoided) && c.New == "true"))
                    {
                        var reason = string.IsNullOrWhiteSpace(p.VoidReason) ? string.Empty : ": " + AuditMask.Short(p.VoidReason);
                        Add(p.BusinessId, AuditActions.PaymentVoided, $"Voided payment {amount}{reason}", "payment", p.Id,
                            orderId: p.OrderId, customerId: p.CustomerId, changes: d);
                    }
                    else if (d.Count > 0)
                    {
                        Add(p.BusinessId, AuditActions.PaymentUpdated, $"Changed payment {amount}: {Fields(d)}", "payment", p.Id,
                            orderId: p.OrderId, customerId: p.CustomerId, changes: d);
                    }

                    break;
                case EntityState.Deleted:
                    Add(p.BusinessId, AuditActions.PaymentDeleted, $"Removed payment {amount}", "payment", p.Id, orderId: p.OrderId, customerId: p.CustomerId);
                    break;
            }
        }

        void Staff(EntityEntry e, StaffMembership s)
        {
            switch (e.State)
            {
                case EntityState.Added:
                    Add(s.BusinessId, AuditActions.StaffAdded, $"Added team member as {s.Role}", "staff", s.UserId,
                        changes: [new Change(nameof(StaffMembership.Role), null, s.Role)], actorFallback: s.UserId);
                    break;
                case EntityState.Modified:
                    var d = Diff(e);
                    if (d.FirstOrDefault(c => c.Field == nameof(StaffMembership.Role)) is { } role)
                    {
                        Add(s.BusinessId, AuditActions.StaffRoleChanged, $"Role changed {role.Old} → {role.New}", "staff", s.UserId, changes: [role]);
                    }

                    if (d.FirstOrDefault(c => c.Field == nameof(StaffMembership.IsActive)) is { } active)
                    {
                        Add(s.BusinessId, active.New == "true" ? AuditActions.StaffReactivated : AuditActions.StaffDeactivated,
                            active.New == "true" ? "Team member reactivated" : "Team member deactivated", "staff", s.UserId, changes: [active]);
                    }

                    break;
                case EntityState.Deleted:
                    Add(s.BusinessId, AuditActions.StaffRemoved, "Team member removed", "staff", s.UserId);
                    break;
            }
        }

        void BusinessEvents(EntityEntry e, Business b)
        {
            if (e.State == EntityState.Added)
            {
                Add(b.Id, AuditActions.BusinessCreated, $"Created business {AuditMask.Short(b.Name, 60)}", "business", b.Id);
            }
            else if (e.State == EntityState.Modified && Diff(e) is { Count: > 0 } d)
            {
                Add(b.Id, AuditActions.BusinessUpdated, $"Edited business details: {Fields(d)}", "business", b.Id, changes: d);
            }
        }

        // Readable per-garment line changes ("Shirt quantity 1 → 2"); notes stay hidden, measurement values never stored.
        List<Change> LineDetails(string orderId)
        {
            var details = new List<Change>();
            foreach (var e in list.Where(x => x.Entity is OrderLine l && l.OrderId == orderId))
            {
                var l = (OrderLine)e.Entity;
                var garment = GarmentTemplate.DisplayName(l.GarmentTemplate);
                switch (e.State)
                {
                    case EntityState.Added:
                        details.Add(new Change("item added", null, $"{garment} × {l.Quantity} at {Money.Format(l.UnitPrice)}"));
                        break;
                    case EntityState.Deleted:
                        details.Add(new Change("item removed", $"{garment} × {l.Quantity} at {Money.Format(l.UnitPrice)}", null));
                        break;
                    case EntityState.Modified:
                        details.AddRange(Diff(e).Select(c => c with { Field = $"{garment} {Label(c.Field)}" }));
                        break;
                }
            }

            // A 50-line edit stays a readable row: first 20 details, then a count.
            const int MaxLineDetails = 20;
            if (details.Count > MaxLineDetails)
            {
                var more = details.Count - MaxLineDetails;
                details = [.. details.Take(MaxLineDetails), new Change("more item changes", null, more.ToString(System.Globalization.CultureInfo.InvariantCulture))];
            }

            return details;
        }

        void Add(string? businessId, string action, string summary, string? entityType, string? entityId,
            string? orderId = null, string? customerId = null, IReadOnlyList<Change>? changes = null, string? actorFallback = null)
        {
            events.Add(AuditEvent.Create(
                IdGenerator.NewId(), businessId, action, summary, now,
                actorUserId: ctx?.UserId ?? actorFallback ?? registrant?.UserId,
                actorRole: ctx?.UserId is null ? registrant?.Role : ctx.Role,
                entityType: entityType, entityId: entityId, orderId: orderId, customerId: customerId,
                changesJson: changes is { Count: > 0 } ? JsonSerializer.Serialize(changes.Select(c => new { field = c.Field, old = c.Old, @new = c.New }), Json) : null,
                source: ctx?.Source, deviceId: ctx?.DeviceId, ipAddress: ctx?.IpAddress, userAgent: ctx?.UserAgent, correlationId: ctx?.CorrelationId));
        }
    }

    /// <summary>Changed fields of a modified row (masked), skipping bookkeeping columns and unchanged values.</summary>
    public static List<Change> Diff(EntityEntry e)
    {
        var changes = new List<Change>();
        foreach (var p in e.Properties)
        {
            var name = p.Metadata.Name;
            if (!p.IsModified || p.Metadata.IsShadowProperty() || Skipped.Contains(name) || Equals(p.OriginalValue, p.CurrentValue))
            {
                continue;
            }

            if (name == nameof(MeasurementProfile.FieldsJson))
            {
                var names = FieldNames(p.OriginalValue as string, p.CurrentValue as string);
                if (names.Length > 0)
                {
                    changes.Add(new Change("fields", null, names));
                }

                continue;
            }

            changes.Add(new Change(name, Format(name, p.OriginalValue), Format(name, p.CurrentValue)));
        }

        return changes;
    }

    /// <summary>New values of a created row (masked).</summary>
    private static List<Change> Snapshot(EntityEntry e) =>
        e.Properties
            .Where(p => !p.Metadata.IsShadowProperty() && !Skipped.Contains(p.Metadata.Name) &&
                        p.Metadata.Name != nameof(MeasurementProfile.FieldsJson) && p.CurrentValue is not null &&
                        p.Metadata.Name is not ("CustomerId" or "OrderId") && // already the row's links
                        p.Metadata.Name != nameof(Payment.IsVoided))
            .Select(p => new Change(p.Metadata.Name, null, Format(p.Metadata.Name, p.CurrentValue)))
            .ToList();

    internal static string? Format(string field, object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (field.Contains("Phone", StringComparison.Ordinal))
        {
            return AuditMask.Phone(value as string);
        }

        if (HiddenText.Contains(field))
        {
            return AuditMask.Hidden;
        }

        return value switch
        {
            decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset t => Money.DateTime(t), // "22 Oct 2026, 14:05" Accra, as the pages show it
            bool b => b ? "true" : "false",
            string s => AuditMask.Short(s, 120),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => AuditMask.Short(value.ToString(), 120)
        };
    }

    /// <summary>Names of measurement fields whose value differs (values themselves are never stored).</summary>
    internal static string FieldNames(string? oldJson, string? newJson)
    {
        var a = Parse(oldJson);
        var b = Parse(newJson);
        var names = a.Keys.Union(b.Keys, StringComparer.Ordinal)
            .Where(k => !a.TryGetValue(k, out var x) | !b.TryGetValue(k, out var y) || x != y)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        return names.Count > 20 ? string.Join(", ", names.Take(20)) + $" and {names.Count - 20} more" : string.Join(", ", names);

        static Dictionary<string, string> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.ValueKind == JsonValueKind.Object
                    ? doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText(), StringComparer.Ordinal)
                    : [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    private static string Fields(IEnumerable<Change> changes) =>
        AuditMask.Short(string.Join(", ", changes.Select(c => Label(c.Field))), 120);

    private static string Label(string field) => AuditActions.FieldLabel(field);

    private static string LineText((int Added, int Changed, int Removed) c)
    {
        var parts = new List<string>();
        if (c.Added > 0) parts.Add($"{c.Added} added");
        if (c.Changed > 0) parts.Add($"{c.Changed} changed");
        if (c.Removed > 0) parts.Add($"{c.Removed} removed");
        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    private static string? BusinessOf(object entity) => entity switch
    {
        IBusinessScoped s => s.BusinessId,
        Business b => b.Id,
        _ => null
    };
}
