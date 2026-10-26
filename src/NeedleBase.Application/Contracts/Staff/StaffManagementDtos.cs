using System.ComponentModel.DataAnnotations;

namespace NeedleBase.Application.Contracts.Staff;

/// <summary>A team member as the Owner/Manager sees them on the Staff screen.</summary>
public sealed record StaffMemberDto(
    string UserId,
    string Name,
    string Email,
    string Role,
    bool IsActive,
    DateTimeOffset JoinedAtUtc,
    int OpenAssignedOrders,
    bool IsYou);

public sealed record InvitationDto(
    string Id,
    string Email,
    string? Name,
    string Role,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? AcceptedAtUtc);

public sealed record StaffOverviewDto(
    IReadOnlyList<StaffMemberDto> Members,
    IReadOnlyList<InvitationDto> Invitations,
    bool CanManage);

/// <summary>
/// The new invite plus its one-time code (shown once; only a hash is stored). <see cref="Emailed"/>: the code was also
/// queued for email (false when email isn't set up; share it by hand either way).
/// </summary>
public sealed record CreatedInvitationDto(InvitationDto Invitation, string Code, bool Emailed = false);

/// <summary>Move someone's open orders: to another active member, or unassigned when <see cref="ToUserId"/> is empty.</summary>
public sealed class ReassignOrdersRequest
{
    [MaxLength(64)]
    public string? ToUserId { get; set; }
}

/// <summary>How many open orders moved.</summary>
public sealed record ReassignOrdersResultDto(int Moved, string? ToUserId, string? ToName = null);

public sealed class CreateInvitationRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Name { get; set; }

    [Required, MaxLength(32)]
    public string Role { get; set; } = string.Empty;
}

public sealed class ChangeStaffRoleRequest
{
    [Required, MaxLength(32)]
    public string Role { get; set; } = string.Empty;
}

/// <summary>What the invite code holder sees before joining.</summary>
public sealed record InvitePreviewDto(string BusinessName, string Role, string Email, string? Name, DateTimeOffset ExpiresAtUtc);
