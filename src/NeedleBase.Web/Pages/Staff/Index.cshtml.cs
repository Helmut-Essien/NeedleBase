using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Staff;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Staff;

/// <summary>
/// Staff (Owner/Manager view; Owner changes): members with open work, invites, roles and access. An invite's code is
/// shown once, on the page that created it (never put in TempData or the URL).
/// </summary>
public class IndexModel : AppPageModel
{
    private readonly INeedleBaseApi _api;

    public IndexModel(INeedleBaseApi api)
    {
        _api = api;
    }

    public StaffOverviewDto? Overview { get; private set; }

    public bool CanManage => User.CanManageStaff();

    /// <summary>The just-created invite and its one-time code.</summary>
    public CreatedInvitationDto? NewInvite { get; private set; }

    [TempData] public string? Flash { get; set; }

    public string? StatusMessage { get; private set; }

    /// <summary>Error from a form post (shown above the forms, page data still loads).</summary>
    public string? ActionError { get; private set; }

    [BindProperty] public InviteInput Invite { get; set; } = new();

    public sealed class InviteInput
    {
        [Required(ErrorMessage = "Enter their email")]
        [EmailAddress(ErrorMessage = "Enter a valid email")]
        [MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Name { get; set; }

        [Required]
        public string Role { get; set; } = BusinessRole.Tailor;
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        StatusMessage = Flash;
        return await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostInviteAsync(CancellationToken cancellationToken)
    {
        if (!CanManage)
        {
            return Forbidden();
        }

        if (!ModelState.IsValid || !StaffRoles.IsAssignable(Invite.Role))
        {
            ActionError = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
                          ?? "Pick Manager or Tailor.";
            return await LoadAsync(cancellationToken);
        }

        var result = await _api.InviteAsync(new CreateInvitationRequest { Email = Invite.Email.Trim(), Name = Invite.Name, Role = Invite.Role }, cancellationToken);
        if (!result.IsSuccess)
        {
            if (Fail(result) is { } redirect)
            {
                return redirect;
            }

            ActionError = PageError;
            PageError = null;
            CanRetry = true;
            return await LoadAsync(cancellationToken);
        }

        NewInvite = result.Value;
        Invite = new InviteInput();
        ModelState.Clear();
        return await LoadAsync(cancellationToken);
    }

    public Task<IActionResult> OnPostRevokeAsync(string id, CancellationToken cancellationToken) =>
        ActAsync(() => _api.RevokeInviteAsync(id, cancellationToken), "Invite cancelled. That code no longer works.");

    public Task<IActionResult> OnPostRoleAsync(string userId, string role, CancellationToken cancellationToken) =>
        ActAsync(() => _api.ChangeStaffRoleAsync(userId, role, cancellationToken), $"Role changed to {role}. It applies on their next screen.");

    public async Task<IActionResult> OnPostDeactivateAsync(string userId, CancellationToken cancellationToken)
    {
        StaffMemberDto? after = null;
        var result = await ActAsync(async () =>
        {
            var r = await _api.SetStaffActiveAsync(userId, false, cancellationToken);
            after = r.Value;
            return r;
        }, "Access turned off. They're signed out everywhere.");
        if (after is { OpenAssignedOrders: > 0 } m && result is RedirectToPageResult)
        {
            Flash = $"Access turned off. They're signed out everywhere. {m.Name} still has {Orders(m.OpenAssignedOrders)} — move them below.";
        }

        return result;
    }

    /// <summary>Owner: move a member's open orders (toUserId empty = leave unassigned).</summary>
    public async Task<IActionResult> OnPostReassignAsync(string userId, string? toUserId, CancellationToken cancellationToken)
    {
        ReassignOrdersResultDto? moved = null;
        var result = await ActAsync(async () =>
        {
            var r = await _api.ReassignOrdersAsync(userId, toUserId, cancellationToken);
            moved = r.Value;
            return r;
        }, "Orders moved.");
        if (moved is not null && result is RedirectToPageResult)
        {
            var name = moved.ToName;
            Flash = moved.Moved == 0
                ? "No open orders to move."
                : moved.ToUserId is null
                    ? $"{Orders(moved.Moved)} now unassigned. Phones pick this up on their next sync."
                    : $"{Orders(moved.Moved)} moved{(name is null ? "" : " to " + name)}. Phones pick this up on their next sync.";
        }

        return result;
    }

    public static string Orders(int n) => $"{n} open order{(n == 1 ? "" : "s")}";

    /// <summary>Active members (not this one) who can take the orders.</summary>
    public IEnumerable<StaffMemberDto> ReassignTargets(StaffMemberDto from) =>
        Overview?.Members.Where(x => x.IsActive && x.UserId != from.UserId) ?? [];

    public Task<IActionResult> OnPostReactivateAsync(string userId, CancellationToken cancellationToken) =>
        ActAsync(() => _api.SetStaffActiveAsync(userId, true, cancellationToken), "Access turned back on. They can sign in again.");

    private async Task<IActionResult> ActAsync<T>(Func<Task<ApiResult<T>>> call, string done)
    {
        if (!CanManage)
        {
            return Forbidden();
        }

        var result = await call();
        if (!result.IsSuccess)
        {
            if (result.Status == System.Net.HttpStatusCode.Unauthorized)
            {
                return Fail(result)!;
            }

            Flash = null;
            ActionError = result.Message;
            return await LoadAsync(HttpContext.RequestAborted);
        }

        Flash = done;
        return RedirectToPage();
    }

    private IActionResult Forbidden()
    {
        PageError = "Only the owner can change staff.";
        CanRetry = false;
        return Page();
    }

    private async Task<IActionResult> LoadAsync(CancellationToken cancellationToken)
    {
        if (!User.CanSeeStaff())
        {
            PageError = "Staff is only shown to the owner and managers.";
            CanRetry = false;
            return Page();
        }

        var result = await _api.GetStaffAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            return Fail(result) ?? Page();
        }

        Overview = result.Value;
        Loaded = true;
        return Page();
    }
}
