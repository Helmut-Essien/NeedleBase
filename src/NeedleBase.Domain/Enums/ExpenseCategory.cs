namespace NeedleBase.Domain.Enums;

/// <summary>
/// What the shop spent money on. Stable string codes (extensible like <see cref="PaymentMethod"/>); labels are the
/// plain words shown on screen.
/// </summary>
public static class ExpenseCategory
{
    public const string Materials = "Materials";
    public const string StaffPay = "StaffPay";
    public const string RentAndBills = "RentAndBills";
    public const string Equipment = "Equipment";
    public const string Transport = "Transport";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All = [Materials, StaffPay, RentAndBills, Equipment, Transport, Other];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);

    public static string DisplayName(string? value) => value switch
    {
        Materials => "Fabric and materials",
        StaffPay => "Staff pay",
        RentAndBills => "Rent, light and water",
        Equipment => "Machines and repairs",
        Transport => "Transport",
        _ => "Other"
    };
}
