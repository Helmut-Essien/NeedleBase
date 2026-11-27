namespace NeedleBase.Domain.Common;

/// <summary>Shared rules for cedi amounts on money records (payments, expenses).</summary>
public static class MoneyRules
{
    /// <summary>Largest single money record: the largest possible order (50 lines × GH₵1,000,000).</summary>
    public const decimal AmountMax = 50_000_000m;

    /// <summary>Null when valid. Amounts are cedi with at most 2 decimals (never rounded silently).</summary>
    public static string? CheckAmount(decimal amount)
    {
        if (amount <= 0)
        {
            return "Amount must be more than 0.";
        }

        if (amount > AmountMax)
        {
            return $"Amount can be at most GH₵ {AmountMax:N0}.";
        }

        return decimal.Round(amount, 2) != amount ? "Amount can have at most 2 decimal places." : null;
    }
}
