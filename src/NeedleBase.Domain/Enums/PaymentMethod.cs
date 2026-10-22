namespace NeedleBase.Domain.Enums;

/// <summary>
/// How the customer paid. NeedleBase records payments; it doesn't process them (no provider confirmation), so a
/// MoMo payment is "recorded", never "confirmed".
/// </summary>
public static class PaymentMethod
{
    public const string Cash = "Cash";
    public const string MoMo = "MoMo";
    public const string Bank = "Bank";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All = [Cash, MoMo, Bank, Other];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);

    public static string DisplayName(string? value) => value switch
    {
        MoMo => "Mobile Money",
        Bank => "Bank transfer",
        Cash => "Cash",
        _ => "Other"
    };
}
