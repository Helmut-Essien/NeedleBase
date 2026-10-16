using System.ComponentModel.DataAnnotations;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Contracts.Measurements;

public sealed record MeasurementListItemDto(
    string Id,
    string CustomerId,
    string GarmentTemplate,
    string Unit,
    DateTimeOffset MeasuredAtUtc,
    DateTimeOffset DateModifiedUtc)
{
    public string GarmentTemplateDisplay =>
        Domain.Enums.GarmentTemplate.DisplayName(GarmentTemplate);
}

public sealed record MeasurementDetailDto(
    string Id,
    string BusinessId,
    string CustomerId,
    string GarmentTemplate,
    string Unit,
    IReadOnlyDictionary<string, decimal?> Fields,
    string? Notes,
    DateTimeOffset MeasuredAtUtc,
    DateTimeOffset DateModifiedUtc);

public sealed class CreateMeasurementRequest
{
    public CreateMeasurementRequest()
    {
    }

    /// <summary>Optional client ULID for offline idempotency.</summary>
    [MaxLength(FieldLimits.Id)]
    public string? Id { get; set; }

    [Required, MaxLength(FieldLimits.GarmentTemplate)]
    public string GarmentTemplate { get; set; } = string.Empty;

    [Required, MaxLength(FieldLimits.MeasurementUnit)]
    public string Unit { get; set; } = "cm";

    [Required]
    public Dictionary<string, decimal?> Fields { get; set; } = new();

    [MaxLength(FieldLimits.Notes)]
    public string? Notes { get; set; }

    public DateTimeOffset? MeasuredAtUtc { get; set; }
}

public sealed class UpdateMeasurementRequest
{
    public UpdateMeasurementRequest()
    {
    }

    [Required, MaxLength(FieldLimits.GarmentTemplate)]
    public string GarmentTemplate { get; set; } = string.Empty;

    [Required, MaxLength(FieldLimits.MeasurementUnit)]
    public string Unit { get; set; } = "cm";

    [Required]
    public Dictionary<string, decimal?> Fields { get; set; } = new();

    [MaxLength(FieldLimits.Notes)]
    public string? Notes { get; set; }

    public DateTimeOffset? MeasuredAtUtc { get; set; }
}
