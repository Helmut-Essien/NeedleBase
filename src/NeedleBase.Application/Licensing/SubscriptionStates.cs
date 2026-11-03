namespace NeedleBase.Application.Licensing;

public static class SubscriptionStates
{
    /// <summary>No key yet (shops made before licensing). Never locked; the Owner sees a banner.</summary>
    public const string NotAttached = "not_attached";

    /// <summary>The stored key can't be decrypted (Data Protection keys lost). Never locked; Owner re-attaches.</summary>
    public const string NeedsReattach = "needs_reattach";

    public const string Active = "active";
    public const string Trial = "trial";
    public const string Complimentary = "complimentary";

    /// <summary>Platform says not valid; still in grace (banner, full use).</summary>
    public const string Problem = "problem";

    /// <summary>Platform says not valid and grace is over: view and sync only.</summary>
    public const string ReadOnly = "read_only";
}
