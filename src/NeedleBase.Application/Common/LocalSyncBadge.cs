namespace NeedleBase.Application.Common;

/// <summary>
/// Per-row sync badge on the phone's lists: "Not synced yet" while the row has a queued change, "Needs attention"
/// when that change failed or conflicted. The server never sets it (list DTOs leave it null).
/// </summary>
public static class LocalSyncBadge
{
    public const string Pending = "pending";
    public const string Attention = "attention";

    public static string? Text(string? state) => state switch
    {
        Pending => "Not synced yet",
        Attention => "Needs attention",
        _ => null
    };

    /// <summary>Worst state across a row's queued ops (queue states: pending / failed / conflict).</summary>
    public static string? Combine(string? current, string queueState) =>
        current == Attention || queueState is "failed" or "conflict" ? Attention : Pending;
}
