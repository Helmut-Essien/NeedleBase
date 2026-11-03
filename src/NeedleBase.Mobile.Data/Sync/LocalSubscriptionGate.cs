using NeedleBase.Application.Contracts.Licensing;

namespace NeedleBase.Mobile.Data.Sync;

/// <summary>
/// The shop's license as last seen by this phone (from sync pull; the app keeps a copy across restarts). When it says
/// read-only, local saves of customers, measurements, orders and payments are refused with a plain message. Viewing,
/// and syncing changes made before the pause, keep working. Unknown (never pulled) means unlocked: the phone never
/// locks on its own guess — the server decides.
/// </summary>
public static class LocalSubscriptionGate
{
    private static readonly object Gate = new();
    private static string? _businessId;
    private static SubscriptionStatusDto? _status;

    public static event EventHandler? Changed;

    public static SubscriptionStatusDto? Current
    {
        get { lock (Gate) { return _status; } }
    }

    public static string? BusinessId
    {
        get { lock (Gate) { return _businessId; } }
    }

    public static bool IsReadOnly
    {
        get { lock (Gate) { return _status?.IsReadOnly == true; } }
    }

    /// <summary>Stores the latest status for a shop. Returns true when anything visible changed.</summary>
    public static bool Set(string businessId, SubscriptionStatusDto? status)
    {
        bool changed;
        lock (Gate)
        {
            changed = !string.Equals(_businessId, businessId, StringComparison.Ordinal) || !Equals(_status, status);
            _businessId = businessId;
            _status = status;
        }

        if (changed)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }

        return changed;
    }

    /// <summary>Sign-out or account switch.</summary>
    public static void Clear()
    {
        bool had;
        lock (Gate)
        {
            had = _status is not null || _businessId is not null;
            _businessId = null;
            _status = null;
        }

        if (had)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}

/// <summary>Thrown by a local save while the shop is read-only; services turn it into a plain message.</summary>
public sealed class SubscriptionReadOnlyException : InvalidOperationException
{
    public SubscriptionReadOnlyException()
        : base(NeedleBase.Application.Licensing.LicenseErrors.ReadOnlyMessage)
    {
    }
}
