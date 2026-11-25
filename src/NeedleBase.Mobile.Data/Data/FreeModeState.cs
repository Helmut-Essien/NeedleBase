namespace NeedleBase.Mobile.Data.Data;

/// <summary>
/// One row per Free (phone-only) shop on this device. Lifetime counters never go down when rows are deleted.
/// </summary>
public sealed class FreeModeState
{
    public string BusinessId { get; set; } = string.Empty;
    public string ShopName { get; set; } = string.Empty;
    public int CustomersEverCreated { get; set; }
    public int OrdersEverCreated { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
