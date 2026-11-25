namespace NeedleBase.Mobile.Data.Sync;

/// <summary>
/// Free (phone only) session for this device: no sync queue, no hosted API. Set when Free starts;
/// cleared on sign-out or when an online account takes over.
/// </summary>
public static class LocalFreeModeGate
{
    private static readonly object Gate = new();
    private static string? _businessId;

    public static event EventHandler? Changed;

    public static bool IsActive
    {
        get { lock (Gate) { return _businessId is not null; } }
    }

    public static string? BusinessId
    {
        get { lock (Gate) { return _businessId; } }
    }

    public static bool IsFreeBusiness(string? businessId)
    {
        if (string.IsNullOrEmpty(businessId))
        {
            return false;
        }

        lock (Gate)
        {
            return string.Equals(_businessId, businessId, StringComparison.Ordinal);
        }
    }

    public static void Set(string businessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        bool changed;
        lock (Gate)
        {
            changed = !string.Equals(_businessId, businessId, StringComparison.Ordinal);
            _businessId = businessId;
        }

        if (changed)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static void Clear()
    {
        bool had;
        lock (Gate)
        {
            had = _businessId is not null;
            _businessId = null;
        }

        if (had)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
