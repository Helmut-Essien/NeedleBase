using NeedleBase.Domain.Enums;

namespace NeedleBase.Domain.Entities;

/// <summary>Links a user to a Business with a workspace role.</summary>
public class StaffMembership : Entity, IBusinessScoped
{
    public string BusinessId { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string Role { get; private set; } = BusinessRole.Tailor;
    public DateTimeOffset JoinedAtUtc { get; private set; }
    public bool IsActive { get; private set; } = true;

    private StaffMembership()
    {
    }

    public static StaffMembership Create(
        string id,
        string businessId,
        string userId,
        string role,
        DateTimeOffset joinedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (!BusinessRole.IsValid(role))
        {
            throw new ArgumentException($"Invalid business role: {role}", nameof(role));
        }

        return new StaffMembership
        {
            Id = id,
            BusinessId = businessId,
            UserId = userId,
            Role = role,
            JoinedAtUtc = joinedAtUtc,
            IsActive = true
        };
    }
}
