namespace NeedleBase.Application.Licensing;

/// <summary>
/// Free (phone only) caps. Lifetime counters: deleting a customer or order never frees a slot.
/// Not a Platform plan — Free never calls Platform.
/// </summary>
public static class FreePlanLimits
{
    public const int MaxCustomersEver = 10;
    public const int MaxOrdersEver = 20;

    public static string CustomerLimitMessage(int used) =>
        used >= MaxCustomersEver
            ? $"Free on this phone allows {MaxCustomersEver} customers. You've used them all. Delete does not free a slot — register a shop with a licence key to keep going online."
            : $"Free on this phone: {used} of {MaxCustomersEver} customers used.";

    public static string OrderLimitMessage(int used) =>
        used >= MaxOrdersEver
            ? $"Free on this phone allows {MaxOrdersEver} orders. You've used them all. Delete does not free a slot — register a shop with a licence key to keep going online."
            : $"Free on this phone: {used} of {MaxOrdersEver} orders used.";

    public static string CustomerLimitReachedMessage =>
        $"Free on this phone allows {MaxCustomersEver} customers ever. You've reached that limit. Deleting a customer does not free a slot.";

    public static string OrderLimitReachedMessage =>
        $"Free on this phone allows {MaxOrdersEver} orders ever. You've reached that limit. Deleting an order does not free a slot.";
}
