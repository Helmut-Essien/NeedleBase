using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Domain.Entities;

namespace NeedleBase.Application.Sync;

/// <summary>Entity ↔ sync DTO mapping shared by the Api and the mobile sync engine.</summary>
public static class SyncMapper
{
    public static SyncCustomerDto ToDto(Customer c) =>
        new(c.Id, c.Name, c.Phone, c.Notes, c.PhotoRef,
            SyncTime.Normalize(c.CreatedAtUtc), SyncTime.Normalize(c.DateModifiedUtc));

    public static SyncMeasurementDto ToDto(MeasurementProfile m) =>
        new(m.Id, m.CustomerId, m.GarmentTemplate, m.Unit, m.FieldsJson, m.Notes,
            SyncTime.Normalize(m.MeasuredAtUtc), SyncTime.Normalize(m.DateModifiedUtc));

    public static SyncOrderLineDto ToDto(OrderLine l) =>
        new(l.Id, l.GarmentTemplate, l.Quantity, l.UnitPrice, l.MeasurementProfileId, l.Unit,
            l.MeasurementFieldsJson, l.Notes, l.SortOrder,
            l.MeasurementTakenAtUtc is { } taken ? SyncTime.Normalize(taken) : null);

    public static SyncOrderDto ToDto(Order o, IEnumerable<OrderLine> lines) =>
        new(o.Id, o.CustomerId, o.DisplayRef, o.Status, o.DueDate, o.FittingDate, o.AssignedToUserId,
            o.TotalAmount, o.Notes, SyncTime.Normalize(o.CreatedAtUtc), SyncTime.Normalize(o.DateModifiedUtc),
            lines.OrderBy(l => l.SortOrder).Select(ToDto).ToList());

    public static SyncPaymentDto ToDto(Payment p) =>
        new(p.Id, p.OrderId, p.CustomerId, p.Amount, p.Method, p.Note, SyncTime.Normalize(p.RecordedAtUtc),
            p.IsVoided, p.VoidedAtUtc is { } voided ? SyncTime.Normalize(voided) : null, p.VoidReason,
            SyncTime.Normalize(p.CreatedAtUtc), SyncTime.Normalize(p.DateModifiedUtc));
}
