using NeedleBase.Domain.Enums;

namespace NeedleBase.Domain.Entities;

/// <summary>
/// Server tombstone so devices can remove rows deleted elsewhere (sync pull).
/// Written automatically by the server DbContext for every synced entity delete.
/// </summary>
public class DeletedItem : Entity, IBusinessScoped
{
    public string BusinessId { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public DateTimeOffset DeletedAtUtc { get; private set; }

    private DeletedItem()
    {
    }

    public static DeletedItem Create(string id, string businessId, string entityType, string entityId, DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        if (!SyncEntityType.IsValid(entityType))
        {
            throw new ArgumentException($"Unknown sync entity type: {entityType}", nameof(entityType));
        }

        return new DeletedItem
        {
            Id = id,
            BusinessId = businessId,
            EntityType = entityType,
            EntityId = entityId,
            DeletedAtUtc = utcNow
        };
    }
}
