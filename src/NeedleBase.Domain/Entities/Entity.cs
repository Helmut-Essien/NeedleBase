namespace NeedleBase.Domain.Entities;

/// <summary>Base for domain entities. Primary keys are ULID strings (NUlid), never int/Guid.</summary>
public abstract class Entity
{
    public string Id { get; protected set; } = string.Empty;
}
