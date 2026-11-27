namespace NeedleBase.Domain.Enums;

/// <summary>Entity type names used by sync operations and <see cref="Entities.DeletedItem"/> tombstones.</summary>
public static class SyncEntityType
{
    public const string Customer = "customer";
    public const string Measurement = "measurement";
    public const string Order = "order";
    public const string OrderLine = "orderLine";
    public const string Payment = "payment";

    /// <summary>Expense (plans step 7b-2). Upsert only (never deleted, void instead); pulled in cursor phase 5.</summary>
    public const string Expense = "expense";

    public static readonly IReadOnlyList<string> All = [Customer, Measurement, Order, OrderLine, Payment, Expense];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
