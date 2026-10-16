namespace NeedleBase.Domain.Common;

/// <summary>
/// Shared max lengths — keep Domain, EF, DTOs, and MAUI validation aligned.
/// </summary>
public static class FieldLimits
{
    public const int Id = 26;
    public const int CustomerName = 200;
    public const int Phone = 40;
    public const int Notes = 2000;
    public const int PhotoRef = 500;
    public const int GarmentTemplate = 40;
    public const int MeasurementUnit = 20;
    public const int FieldsJson = 8000;
}
