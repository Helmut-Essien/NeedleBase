using NeedleBase.Domain.Common;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Domain.Entities;

/// <summary>Garment line on an order, with a frozen measurement snapshot.</summary>
public class OrderLine : Entity, IBusinessScoped
{
    public string BusinessId { get; private set; } = string.Empty;
    public string OrderId { get; private set; } = string.Empty;
    public string GarmentTemplate { get; private set; } = string.Empty;
    public int Quantity { get; private set; } = 1;
    public decimal UnitPrice { get; private set; }

    /// <summary>Optional source profile id (snapshot is authoritative).</summary>
    public string? MeasurementProfileId { get; private set; }

    public string Unit { get; private set; } = Enums.MeasurementUnit.Centimetres;
    public string MeasurementFieldsJson { get; private set; } = "{}";
    public string? Notes { get; private set; }
    public int SortOrder { get; private set; }

    private OrderLine()
    {
    }

    public static OrderLine Create(
        string id,
        string businessId,
        string orderId,
        string garmentTemplate,
        int quantity,
        decimal unitPrice,
        string? measurementProfileId,
        string unit,
        string measurementFieldsJson,
        string? notes,
        int sortOrder = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        var line = new OrderLine
        {
            Id = id,
            BusinessId = businessId,
            OrderId = orderId
        };
        line.ApplyDetails(
            garmentTemplate,
            quantity,
            unitPrice,
            measurementProfileId,
            unit,
            measurementFieldsJson,
            notes,
            sortOrder);
        return line;
    }

    internal void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    private void ApplyDetails(
        string garmentTemplate,
        int quantity,
        decimal unitPrice,
        string? measurementProfileId,
        string unit,
        string measurementFieldsJson,
        string? notes,
        int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(garmentTemplate);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        ArgumentException.ThrowIfNullOrWhiteSpace(measurementFieldsJson);

        garmentTemplate = garmentTemplate.Trim().ToLowerInvariant();
        unit = unit.Trim().ToLowerInvariant();

        if (!Enums.GarmentTemplate.IsValid(garmentTemplate))
        {
            throw new ArgumentException($"Unknown garment template: {garmentTemplate}", nameof(garmentTemplate));
        }

        if (!Enums.MeasurementUnit.IsValid(unit))
        {
            throw new ArgumentException("Unit must be cm or in.", nameof(unit));
        }

        if (quantity < 1 || quantity > FieldLimits.OrderLineQuantityMax)
        {
            throw new ArgumentException(
                $"Quantity must be between 1 and {FieldLimits.OrderLineQuantityMax}.",
                nameof(quantity));
        }

        if (unitPrice < 0)
        {
            throw new ArgumentException("Unit price cannot be negative.", nameof(unitPrice));
        }

        if (garmentTemplate.Length > FieldLimits.GarmentTemplate)
        {
            throw new ArgumentException(
                $"Garment template must be at most {FieldLimits.GarmentTemplate} characters.",
                nameof(garmentTemplate));
        }

        if (measurementFieldsJson.Length > FieldLimits.FieldsJson)
        {
            throw new ArgumentException(
                $"Measurement fields must be at most {FieldLimits.FieldsJson} characters.",
                nameof(measurementFieldsJson));
        }

        measurementProfileId = string.IsNullOrWhiteSpace(measurementProfileId)
            ? null
            : measurementProfileId.Trim();
        if (measurementProfileId is { Length: > FieldLimits.Id })
        {
            throw new ArgumentException(
                $"Measurement profile id must be at most {FieldLimits.Id} characters.",
                nameof(measurementProfileId));
        }

        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (notes is { Length: > FieldLimits.Notes })
        {
            throw new ArgumentException($"Notes must be at most {FieldLimits.Notes} characters.", nameof(notes));
        }

        GarmentTemplate = garmentTemplate;
        Quantity = quantity;
        UnitPrice = decimal.Round(unitPrice, 2, MidpointRounding.AwayFromZero);
        MeasurementProfileId = measurementProfileId;
        Unit = unit;
        MeasurementFieldsJson = measurementFieldsJson;
        Notes = notes;
        SortOrder = sortOrder;
    }

    public decimal LineTotal => decimal.Round(UnitPrice * Quantity, 2, MidpointRounding.AwayFromZero);
}
