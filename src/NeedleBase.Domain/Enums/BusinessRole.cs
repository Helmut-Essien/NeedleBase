namespace NeedleBase.Domain.Enums;

/// <summary>Role within a Business workspace (claim value + StaffMembership).</summary>
public static class BusinessRole
{
    public const string Owner = "Owner";
    public const string Manager = "Manager";
    public const string Tailor = "Tailor";

    public static readonly IReadOnlyList<string> All = [Owner, Manager, Tailor];

    public static bool IsValid(string? role) =>
        role is Owner or Manager or Tailor;
}
