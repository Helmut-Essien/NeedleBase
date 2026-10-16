namespace NeedleBase.Domain.Entities;

/// <summary>Tenant-scoped data must carry BusinessId. Queries must always filter by it.</summary>
public interface IBusinessScoped
{
    string BusinessId { get; }
}
