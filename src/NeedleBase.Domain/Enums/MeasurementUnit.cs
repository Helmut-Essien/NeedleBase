namespace NeedleBase.Domain.Enums;

public static class MeasurementUnit
{
    public const string Centimetres = "cm";
    public const string Inches = "in";

    public static readonly IReadOnlyList<string> All = [Centimetres, Inches];

    public static bool IsValid(string? unit) => unit is Centimetres or Inches;
}
