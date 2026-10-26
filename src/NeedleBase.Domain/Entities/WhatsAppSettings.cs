using NeedleBase.Domain.Enums;

namespace NeedleBase.Domain.Entities;

/// <summary>
/// Per-business automatic WhatsApp messages (Meta Cloud API). One row per business (Id = BusinessId).
/// Off until the Owner/Manager turns it on; only the statuses listed here send a message.
/// </summary>
public class WhatsAppSettings : Entity, IBusinessScoped
{
    public const int StatusesMax = 100;

    public string BusinessId { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }

    /// <summary>Comma-separated order statuses that send a message (e.g. "Ready").</summary>
    public string Statuses { get; private set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public string? UpdatedByUserId { get; private set; }

    private WhatsAppSettings()
    {
    }

    public static WhatsAppSettings Create(string businessId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        return new WhatsAppSettings { Id = businessId, BusinessId = businessId, UpdatedAtUtc = now };
    }

    public IReadOnlyList<string> StatusList =>
        Statuses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool Sends(string status) => Enabled && StatusList.Contains(status, StringComparer.Ordinal);

    public void Update(bool enabled, IEnumerable<string> statuses, string? userId, DateTimeOffset now)
    {
        var clean = statuses
            .Where(s => OrderStatus.IsValid(s) && s != OrderStatus.New)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => OrderStatus.All.ToList().IndexOf(s))
            .ToList();
        Enabled = enabled;
        Statuses = string.Join(',', clean);
        UpdatedByUserId = userId;
        UpdatedAtUtc = now;
    }
}
