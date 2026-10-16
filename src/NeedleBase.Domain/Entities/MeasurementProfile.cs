using NeedleBase.Domain.Common;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Domain.Entities;

/// <summary>Dated measurement capture for a customer and garment template.</summary>
public class MeasurementProfile : Entity, IBusinessScoped
{
    public string BusinessId { get; private set; } = string.Empty;
    public string CustomerId { get; private set; } = string.Empty;
    public string GarmentTemplate { get; private set; } = string.Empty;
    public string Unit { get; private set; } = MeasurementUnit.Centimetres;
    public string FieldsJson { get; private set; } = "{}";
    public string? Notes { get; private set; }
    public DateTimeOffset MeasuredAtUtc { get; private set; }
    public DateTimeOffset DateModifiedUtc { get; private set; }

    private MeasurementProfile()
    {
    }

    public static MeasurementProfile Create(
        string id,
        string businessId,
        string customerId,
        string garmentTemplate,
        string unit,
        string fieldsJson,
        string? notes,
        DateTimeOffset measuredAtUtc,
        DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);

        var profile = new MeasurementProfile
        {
            Id = id,
            BusinessId = businessId,
            CustomerId = customerId,
            MeasuredAtUtc = measuredAtUtc,
            DateModifiedUtc = utcNow
        };
        profile.ApplyDetails(garmentTemplate, unit, fieldsJson, notes, measuredAtUtc, utcNow);
        return profile;
    }

    public void Update(
        string garmentTemplate,
        string unit,
        string fieldsJson,
        string? notes,
        DateTimeOffset measuredAtUtc,
        DateTimeOffset utcNow)
    {
        ApplyDetails(garmentTemplate, unit, fieldsJson, notes, measuredAtUtc, utcNow);
    }

    private void ApplyDetails(
        string garmentTemplate,
        string unit,
        string fieldsJson,
        string? notes,
        DateTimeOffset measuredAtUtc,
        DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(garmentTemplate);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldsJson);

        garmentTemplate = garmentTemplate.Trim().ToLowerInvariant();
        unit = unit.Trim().ToLowerInvariant();

        if (!Enums.GarmentTemplate.IsValid(garmentTemplate))
        {
            throw new ArgumentException($"Unknown garment template: {garmentTemplate}", nameof(garmentTemplate));
        }

        if (!MeasurementUnit.IsValid(unit))
        {
            throw new ArgumentException($"Unit must be cm or in.", nameof(unit));
        }

        if (garmentTemplate.Length > FieldLimits.GarmentTemplate)
        {
            throw new ArgumentException(
                $"Garment template must be at most {FieldLimits.GarmentTemplate} characters.",
                nameof(garmentTemplate));
        }

        if (fieldsJson.Length > FieldLimits.FieldsJson)
        {
            throw new ArgumentException(
                $"Measurement fields must be at most {FieldLimits.FieldsJson} characters.",
                nameof(fieldsJson));
        }

        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (notes is { Length: > FieldLimits.Notes })
        {
            throw new ArgumentException($"Notes must be at most {FieldLimits.Notes} characters.", nameof(notes));
        }

        GarmentTemplate = garmentTemplate;
        Unit = unit;
        FieldsJson = fieldsJson;
        Notes = notes;
        MeasuredAtUtc = measuredAtUtc;
        DateModifiedUtc = utcNow;
    }
}
