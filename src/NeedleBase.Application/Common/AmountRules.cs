using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Common;

/// <summary>
/// Input rules for new prices and measurements (API + mobile). They apply when a user enters or changes a value;
/// stored rows that predate them still load, pull and display (the domain stays lenient for existing data).
/// </summary>
public static class AmountRules
{
    public const decimal UnitPriceMax = 1_000_000m;
    public const int OrderLinesMax = 50;
    public const decimal MaxCentimetres = 300m;
    public const decimal MaxInches = 118m; // 300 cm ≈ 118.1 in: same physical cap in both units

    public static string? CheckUnitPrice(decimal price)
    {
        if (price <= 0)
        {
            return "Unit price must be more than 0.";
        }

        if (price > UnitPriceMax)
        {
            return $"Unit price can be at most GH₵ {UnitPriceMax:N0}.";
        }

        return decimal.Round(price, 2) != price ? "Unit price can have at most 2 decimal places." : null;
    }

    public static decimal MaxFor(string? unit) =>
        string.Equals(unit?.Trim(), MeasurementUnit.Inches, StringComparison.OrdinalIgnoreCase) ? MaxInches : MaxCentimetres;

    /// <summary>Null when valid; otherwise a message naming the field. Blank (null) fields are allowed.</summary>
    public static string? CheckMeasurement(string? unit, string field, decimal? value)
    {
        if (value is not { } v)
        {
            return null;
        }

        var max = MaxFor(unit);
        var label = GarmentTemplateFields.DisplayLabel(field);
        if (v <= 0)
        {
            return $"{label} must be more than 0.";
        }

        return v > max ? $"{label} can be at most {max:0} {unit?.Trim().ToLowerInvariant()}." : null;
    }

    /// <summary>Throws <see cref="ArgumentException"/> (services map it to a validation error).</summary>
    public static void EnsureMeasurements(string? unit, IReadOnlyDictionary<string, decimal?>? fields)
    {
        if (fields is null)
        {
            return;
        }

        foreach (var (key, value) in fields)
        {
            if (CheckMeasurement(unit, key, value) is { } error)
            {
                throw new ArgumentException(error, nameof(fields));
            }
        }
    }

    /// <summary>
    /// Update variant: only values that differ from what is stored are checked (all of them if the unit
    /// changed), so a profile saved before the limits existed stays editable (notes, other fields).
    /// </summary>
    public static void EnsureChangedMeasurements(
        string? unit,
        IReadOnlyDictionary<string, decimal?>? fields,
        string? previousUnit,
        IReadOnlyDictionary<string, decimal?> previousFields)
    {
        if (fields is null)
        {
            return;
        }

        var unitChanged = !string.Equals(unit?.Trim(), previousUnit?.Trim(), StringComparison.OrdinalIgnoreCase);
        var changed = fields
            .Where(f => unitChanged || !previousFields.TryGetValue(f.Key, out var old) || old != f.Value)
            .ToDictionary(f => f.Key, f => f.Value);
        EnsureMeasurements(unit, changed);
    }
}
