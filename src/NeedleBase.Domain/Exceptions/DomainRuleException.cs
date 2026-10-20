namespace NeedleBase.Domain.Exceptions;

/// <summary>
/// A business rule was broken. <see cref="Code"/> is a stable machine code
/// (e.g. <see cref="DomainErrorCodes.OrderLocked"/>) that callers map to HTTP status and user copy.
/// </summary>
public sealed class DomainRuleException : ArgumentException
{
    public DomainRuleException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

public static class DomainErrorCodes
{
    /// <summary>Collected/Cancelled orders cannot be edited or moved.</summary>
    public const string OrderLocked = "ORDER_LOCKED";

    /// <summary>Unknown order status value.</summary>
    public const string OrderStatusInvalid = "ORDER_STATUS_INVALID";
}
