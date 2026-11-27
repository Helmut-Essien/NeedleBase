namespace NeedleBase.Domain.Enums;

/// <summary>Entity type names used by sync operations and <see cref="Entities.DeletedItem"/> tombstones.</summary>
public static class SyncEntityType
{
    public const string Customer = "customer";
    public const string Measurement = "measurement";
    public const string Order = "order";
    public const string OrderLine = "orderLine";
    public const string Payment = "payment";

    /// <summary>
    /// Expense (plans step 7b-2). Used by the server to stamp sync columns; deliberately not in <see cref="All"/>
    /// until expense sync push/pull is built, so no sync operation or tombstone for it is accepted before then.
    /// </summary>
    public const string Expense = "expense";

    public static readonly IReadOnlyList<string> All = [Customer, Measurement, Order, OrderLine, Payment];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
