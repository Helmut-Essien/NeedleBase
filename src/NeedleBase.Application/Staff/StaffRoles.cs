using NeedleBase.Domain.Enums;

namespace NeedleBase.Application.Staff;

/// <summary>Plain-language role copy and the role rules shared by API, web and mobile.</summary>
public static class StaffRoles
{
    /// <summary>Roles an Owner can give (there is exactly one Owner per business).</summary>
    public static readonly IReadOnlyList<string> Assignable = [BusinessRole.Manager, BusinessRole.Tailor];

    public static bool IsAssignable(string? role) => role is BusinessRole.Manager or BusinessRole.Tailor;

    /// <summary>Owner and Manager see the Staff screen; only the Owner changes it.</summary>
    public static bool CanView(string? role) => role is BusinessRole.Owner or BusinessRole.Manager;

    public static bool CanManage(string? role) => role is BusinessRole.Owner;

    public static string Summary(string? role) => role switch
    {
        BusinessRole.Owner => "Runs everything: staff, payments and the business.",
        BusinessRole.Manager => "Manager runs the shop day to day: customers, orders and payments. Can't change staff.",
        BusinessRole.Tailor => "Tailors see work assigned to them and update its progress. No payments or staff.",
        _ => string.Empty
    };
}
