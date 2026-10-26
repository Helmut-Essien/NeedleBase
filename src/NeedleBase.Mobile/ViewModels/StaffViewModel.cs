using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Mail;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Staff;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.ViewModels;

public sealed class StaffRow
{
    public StaffRow(StaffMemberDto member, bool canManage)
    {
        Member = member;
        CanChange = canManage && !member.IsYou && member.Role != BusinessRole.Owner;
    }

    public StaffMemberDto Member { get; }
    public string Name => Member.IsYou ? $"{Member.Name} (you)" : Member.Name;
    public string Email => Member.Email;
    public string RoleText => Member.IsActive ? Member.Role : $"{Member.Role} · access off";
    public string WorkText => Member.OpenAssignedOrders switch
    {
        0 => "No open orders assigned",
        1 => "1 open order assigned",
        var n => $"{n} open orders assigned"
    };
    public bool CanChange { get; }
    public string Description => $"{Name}, {RoleText}, {WorkText}";
}

public sealed class InviteRow
{
    private static readonly CultureInfo Gh = CultureInfo.GetCultureInfo("en-GH");

    public InviteRow(InvitationDto invite, bool canManage)
    {
        Invite = invite;
        CanRevoke = canManage && invite.Status == nameof(InvitationStatus.Pending);
    }

    public InvitationDto Invite { get; }
    public string Title => string.IsNullOrWhiteSpace(Invite.Name) ? Invite.Email : $"{Invite.Name} · {Invite.Email}";
    public string Detail => Invite.Status switch
    {
        nameof(InvitationStatus.Pending) => $"{Invite.Role} · waiting · expires {Invite.ExpiresAtUtc.ToLocalTime().ToString("d MMM", Gh)}",
        nameof(InvitationStatus.Accepted) => $"{Invite.Role} · joined",
        nameof(InvitationStatus.Revoked) => $"{Invite.Role} · cancelled",
        _ => $"{Invite.Role} · expired"
    };
    public bool CanRevoke { get; }
}

/// <summary>Staff (online only): who's on the team, invites, roles and access. Owner changes; Manager views.</summary>
public partial class StaffViewModel : BaseViewModel
{
    private readonly IStaffApi _api;

    public StaffViewModel(IStaffApi api)
    {
        _api = api;
        Title = "Staff";
    }

    /// <summary>UI hooks (set by the page; tests replace them).</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(true);
    public Func<string, string[], Task<string?>> ChooseAction { get; set; } = (_, _) => Task.FromResult<string?>(null);
    public Func<string, Task> ShareText { get; set; } = _ => Task.CompletedTask;

    public ObservableCollection<StaffRow> Members { get; } = new();
    public ObservableCollection<InviteRow> Invites { get; } = new();
    public IReadOnlyList<string> RoleChoices { get; } = StaffRoles.Assignable;

    [ObservableProperty] private bool canManage;
    [ObservableProperty] private bool loaded;
    [ObservableProperty] private bool hasInvites;
    [ObservableProperty] private bool isSolo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? statusMessage;

    [ObservableProperty] private string inviteEmail = string.Empty;
    [ObservableProperty] private string inviteName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InviteRoleSummary))]
    private string inviteRole = BusinessRole.Tailor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewCode))]
    private string? newCode;

    /// <summary>Line under the code: emailed too, or share-by-hand only.</summary>
    [ObservableProperty] private string? newCodeNote;

    [ObservableProperty] private string? newCodeFor;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool HasNewCode => !string.IsNullOrWhiteSpace(NewCode);
    public string InviteRoleSummary => StaffRoles.Summary(InviteRole);

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _api.GetAsync();
            if (!result.IsSuccess || result.Value is null)
            {
                ErrorMessage = result.Error ?? "Couldn't load staff.";
                return;
            }

            ErrorMessage = null;
            Apply(result.Value);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(StaffOverviewDto overview)
    {
        CanManage = overview.CanManage;
        Members.Clear();
        foreach (var m in overview.Members)
        {
            Members.Add(new StaffRow(m, overview.CanManage));
        }

        Invites.Clear();
        foreach (var i in overview.Invitations.Where(i => i.Status != nameof(InvitationStatus.Accepted)))
        {
            Invites.Add(new InviteRow(i, overview.CanManage));
        }

        HasInvites = Invites.Count > 0;
        IsSolo = overview.Members.Count <= 1 && !HasInvites;
        Loaded = true;
    }

    [RelayCommand]
    private async Task SendInviteAsync()
    {
        if (IsBusy || !CanManage)
        {
            return;
        }

        StatusMessage = null;
        var email = InviteEmail.Trim();
        if (!IsEmail(email))
        {
            ErrorMessage = "Enter a valid email for the person you're inviting.";
            return;
        }

        if (!StaffRoles.IsAssignable(InviteRole))
        {
            ErrorMessage = "Pick Manager or Tailor.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _api.InviteAsync(new CreateInvitationRequest
            {
                Email = email,
                Name = string.IsNullOrWhiteSpace(InviteName) ? null : InviteName.Trim(),
                Role = InviteRole
            });
            if (!result.IsSuccess || result.Value is null)
            {
                ErrorMessage = result.Error ?? "Couldn't create the invite.";
                return;
            }

            ErrorMessage = null;
            NewCode = result.Value.Code;
            NewCodeFor = result.Value.Invitation.Email;
            NewCodeNote = result.Value.Emailed
                ? $"We're emailing this code to {NewCodeFor}. Share it too (WhatsApp/SMS) in case the email lands in spam. It's shown only now."
                : $"For {NewCodeFor}. It's shown only now: share it before you leave this screen.";
            InviteEmail = string.Empty;
            InviteName = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
    }

    /// <summary>The message the owner shares (WhatsApp/SMS): what to install and the code. No link needed.</summary>
    public string ShareMessage(string businessName) =>
        $"You're invited to join {businessName} on NeedleBase. Open the NeedleBase app, tap \"Join with an invite code\" and enter: {NewCode}. The code works once and expires in 7 days.";

    [RelayCommand]
    private async Task ShareCodeAsync(string? businessName)
    {
        if (HasNewCode)
        {
            await ShareText(ShareMessage(string.IsNullOrWhiteSpace(businessName) ? "our shop" : businessName));
        }
    }

    [RelayCommand]
    private void DismissCode()
    {
        NewCode = null;
        NewCodeFor = null;
        NewCodeNote = null;
    }

    [RelayCommand]
    private async Task RevokeAsync(InviteRow? row)
    {
        if (row is null || !row.CanRevoke || IsBusy)
        {
            return;
        }

        if (!await Confirm("Cancel invite?", $"The code sent to {row.Invite.Email} will stop working."))
        {
            return;
        }

        await RunAsync(() => _api.RevokeAsync(row.Invite.Id), "Invite cancelled.");
    }

    public const string ActionMakeManager = "Make Manager";
    public const string ActionMakeTailor = "Make Tailor";
    public const string ActionTurnOff = "Turn off access";
    public const string ActionTurnOn = "Turn access back on";
    public const string ActionMoveOrders = "Move their open orders";
    public const string LeaveUnassigned = "Leave unassigned";

    [RelayCommand]
    private async Task ManageAsync(StaffRow? row)
    {
        if (row is null || !row.CanChange || IsBusy)
        {
            return;
        }

        var m = row.Member;
        var actions = new List<string>();
        if (m.IsActive)
        {
            actions.Add(m.Role == BusinessRole.Manager ? ActionMakeTailor : ActionMakeManager);
            actions.Add(ActionTurnOff);
        }
        else
        {
            actions.Add(ActionTurnOn);
        }

        if (m.OpenAssignedOrders > 0)
        {
            actions.Add(ActionMoveOrders);
        }

        var choice = await ChooseAction(m.Name, actions.ToArray());
        switch (choice)
        {
            case ActionMakeManager:
                await RunAsync(() => _api.ChangeRoleAsync(m.UserId, BusinessRole.Manager), $"{m.Name} is now a Manager.");
                break;
            case ActionMakeTailor:
                await RunAsync(() => _api.ChangeRoleAsync(m.UserId, BusinessRole.Tailor), $"{m.Name} is now a Tailor.");
                break;
            case ActionTurnOff:
                var work = m.OpenAssignedOrders > 0 ? $" Next you can move their {Orders(m.OpenAssignedOrders)} to someone else." : string.Empty;
                if (await Confirm($"Turn off access for {m.Name}?", $"They'll be signed out on every phone and can't sign in.{work}") &&
                    await RunAsync(() => _api.SetActiveAsync(m.UserId, false), $"{m.Name} can no longer sign in.") &&
                    m.OpenAssignedOrders > 0)
                {
                    await MoveOrdersAsync(m);
                }

                break;
            case ActionTurnOn:
                await RunAsync(() => _api.SetActiveAsync(m.UserId, true), $"{m.Name} can sign in again.");
                break;
            case ActionMoveOrders:
                await MoveOrdersAsync(m);
                break;
        }
    }

    private static string Orders(int n) => n == 1 ? "1 open order" : $"{n} open orders";

    /// <summary>Choices for moving someone's open orders: other active members (unique labels) plus "Leave unassigned".</summary>
    public IReadOnlyList<(string Label, string? UserId)> ReassignTargets(StaffMemberDto from)
    {
        var targets = Members.Select(r => r.Member).Where(x => x.IsActive && x.UserId != from.UserId).ToList();
        var list = new List<(string, string?)>();
        foreach (var t in targets)
        {
            var label = $"{t.Name} · {t.Role}";
            if (targets.Count(o => $"{o.Name} · {o.Role}" == label) > 1)
            {
                label = $"{t.Name} · {t.Email}";
            }

            list.Add((label, t.UserId));
        }

        list.Add((LeaveUnassigned, null));
        return list;
    }

    private async Task MoveOrdersAsync(StaffMemberDto from)
    {
        var targets = ReassignTargets(from);
        var choice = await ChooseAction($"Move {from.Name}'s {Orders(from.OpenAssignedOrders)} to", targets.Select(t => t.Label).ToArray());
        if (choice is null)
        {
            return;
        }

        var target = targets.FirstOrDefault(t => t.Label == choice);
        if (target.Label is null)
        {
            return;
        }

        IsBusy = true;
        StaffCallResult<ReassignOrdersResultDto> result;
        try
        {
            result = await _api.ReassignOrdersAsync(from.UserId, target.UserId);
        }
        finally
        {
            IsBusy = false;
        }

        if (!result.IsSuccess || result.Value is null)
        {
            ErrorMessage = result.Error ?? "Couldn't move the orders. Try again.";
            await LoadAsync();
            return;
        }

        ErrorMessage = null;
        StatusMessage = result.Value.Moved == 0
            ? $"{from.Name} has no open orders to move."
            : target.UserId is null
                ? $"{Orders(result.Value.Moved)} from {from.Name} are now unassigned."
                : $"Moved {Orders(result.Value.Moved)} from {from.Name} to {result.Value.ToName ?? choice}.";
        await LoadAsync();
    }

    private async Task<bool> RunAsync<T>(Func<Task<StaffCallResult<T>>> call, string done)
    {
        IsBusy = true;
        StaffCallResult<T> result;
        try
        {
            result = await call();
        }
        finally
        {
            IsBusy = false;
        }

        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error;
            return false;
        }

        ErrorMessage = null;
        StatusMessage = done;
        await LoadAsync();
        return true;
    }

    public void ClearSessionState()
    {
        Members.Clear();
        Invites.Clear();
        NewCode = null;
        NewCodeFor = null;
        NewCodeNote = null;
        Loaded = false;
        ErrorMessage = null;
        StatusMessage = null;
    }

    public static bool IsEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Contains(' '))
        {
            return false;
        }

        try
        {
            var address = new MailAddress(value);
            return address.Address == value && value.Contains('.', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
