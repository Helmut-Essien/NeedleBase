using NeedleBase.Application.Contracts.Staff;

namespace NeedleBase.Application.Abstractions;

/// <summary>Result of a staff change: <see cref="ErrorCode"/> is a StaffErrorMessages code, null on success.</summary>
public sealed record StaffResult<T>(T? Value, string? ErrorCode)
{
    public bool Succeeded => ErrorCode is null;
    public static StaffResult<T> Ok(T value) => new(value, null);
    public static StaffResult<T> Fail(string code) => new(default, code);
}

public interface IStaffService
{
    Task<StaffOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<StaffResult<CreatedInvitationDto>> InviteAsync(CreateInvitationRequest request, CancellationToken cancellationToken = default);
    Task<StaffResult<bool>> RevokeInvitationAsync(string invitationId, CancellationToken cancellationToken = default);
    Task<StaffResult<StaffMemberDto>> ChangeRoleAsync(string userId, string role, CancellationToken cancellationToken = default);
    Task<StaffResult<StaffMemberDto>> SetActiveAsync(string userId, bool active, CancellationToken cancellationToken = default);

    /// <summary>
    /// Owner: moves every open (not Collected/Cancelled) order assigned to <paramref name="fromUserId"/> to another active
    /// member, or unassigns them (<paramref name="toUserId"/> null). One transaction; bumps each order's modified time so
    /// phones pull the change and stale offline edits conflict as usual.
    /// </summary>
    Task<StaffResult<ReassignOrdersResultDto>> ReassignOpenOrdersAsync(string fromUserId, string? toUserId, CancellationToken cancellationToken = default);
}
