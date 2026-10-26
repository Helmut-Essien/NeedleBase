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

    /// <summary>Owner-only change of a team member's role. The single Owner role is not handed out or taken away here.</summary>
    public void ChangeRole(string role)
    {
        if (role is not (BusinessRole.Manager or BusinessRole.Tailor))
        {
            throw new ArgumentException($"Role must be {BusinessRole.Manager} or {BusinessRole.Tailor}.", nameof(role));
        }

        if (Role == BusinessRole.Owner)
        {
            throw new InvalidOperationException("The owner's role can't be changed.");
        }

        Role = role;
    }

    public void Deactivate()
    {
        if (Role == BusinessRole.Owner)
        {
            throw new InvalidOperationException("The owner can't be deactivated.");
        }

        IsActive = false;
    }

    public void Reactivate() => IsActive = true;
}
