using NeedleBase.Application.Contracts.Audit;
using NeedleBase.Application.Common;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Contracts.WhatsApp;
using NeedleBase.Application.WhatsApp;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Orders;

/// <summary>
/// One order: lines, status change (409 STALE_UPDATE safe via baseModifiedUtc), assignee name and, for
/// owner/manager, the payment ledger. Order, payments and staff names load in parallel.
/// </summary>
public class DetailsModel : AppPageModel
{
    public const string StaleMessage = "Someone changed this order while you were looking at it. The latest version is shown below. Check it, then change the status again if you still need to.";

    private readonly INeedleBaseApi _api;

    public DetailsModel(INeedleBaseApi api)
    {
        _api = api;
    }

    [BindProperty(SupportsGet = true)] public string Id { get; set; } = string.Empty;

    [BindProperty, Required(ErrorMessage = "Choose a status."), MaxLength(32)]
    public string NewStatus { get; set; } = string.Empty;

    /// <summary>Round-tripped "o" timestamp of the version the user saw.</summary>
    [BindProperty, MaxLength(64)] public string? BaseModified { get; set; }

    public OrderDetailDto? Order { get; private set; }
    public OrderPaymentsDto? Payments { get; private set; }

    /// <summary>Owner/Manager on a plan with expenses: this order's latest expenses + total (null hides the card).</summary>
    public NeedleBase.Application.Contracts.Expenses.ExpenseListResponse? Expenses { get; private set; }

    public const int ExpensePreviewCount = 5;

    /// <summary>"Add expense" is hidden on a read-only shop (the Api refuses it anyway).</summary>
    public bool CanAddExpense { get; private set; }
    public string? AssigneeName { get; private set; }
    public string? StatusMessage { get; private set; }
    public string? StatusError { get; private set; }

    [TempData] public string? Flash { get; set; }

    /// <summary>Owner/Manager: the team member to assign ("" = not assigned). Code-gaps G14.</summary>
    [BindProperty, MaxLength(450)] public string? NewAssignee { get; set; }

    public string? AssignError { get; private set; }

    /// <summary>Active team members for the assign picker (empty when the staff list didn't load).</summary>
    public IReadOnlyList<StaffNameDto> Staff { get; private set; } = Array.Empty<StaffNameDto>();

    /// <summary>The current assignee is no longer an active member: keep them as an option so the picker isn't wrong.</summary>
    public bool AssigneeIsFormer => Order?.AssignedToUserId is { Length: > 0 } id && Staff.All(s => s.UserId != id);

    public IEnumerable<string> StatusChoices => OrderStatus.All.Where(s => s != Order?.Status);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        StatusMessage = Flash;
        return await LoadAsync(cancellationToken) ?? Page();
    }

    public async Task<IActionResult> OnPostStatusAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !OrderStatus.All.Contains(NewStatus))
        {
            StatusError = "Choose a status.";
            return await LoadAsync(cancellationToken) ?? Page();
        }

        DateTimeOffset? baseModified = DateTimeOffset.TryParse(BaseModified, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
        var result = await _api.UpdateOrderStatusAsync(Id, new UpdateOrderStatusRequest { Status = NewStatus, BaseModifiedUtc = baseModified }, cancellationToken);
        if (result.IsSuccess)
        {
            Flash = $"Status changed to {OrderStatus.DisplayName(NewStatus)}.";
            return RedirectToPage(new { id = Id }); // PRG: a refresh never re-posts
        }

        if (result.Status == System.Net.HttpStatusCode.Unauthorized)
        {
            return Fail(result)!;
        }

        ModelState.Clear(); // the form re-renders from the freshly loaded order
        var reload = await LoadAsync(cancellationToken);
        if (result.Code == StaleUpdate.Code && Order?.Status == NewStatus)
        {
            // Double submit (or someone made the same move): the order already has the status asked for.
            StatusMessage = $"Status is {OrderStatus.DisplayName(NewStatus)}.";
            return reload ?? Page();
        }

        StatusError = StatusErrorFor(result);
        return reload ?? Page();
    }

    /// <summary>Owner/Manager: assign or unassign (the Api checks role, team membership and the read-only guard).</summary>
    public async Task<IActionResult> OnPostAssignAsync(CancellationToken cancellationToken)
    {
        // NewStatus is [Required] for the status form; only this form's field matters here.
        if (!User.CanEditOrders() || NewAssignee is { Length: > 450 })
        {
            ModelState.Clear();
            var denied = await LoadAsync(cancellationToken);
            AssignError = User.CanEditOrders() ? "Pick someone from the list." : "Only the owner or a manager can assign orders.";
            return denied ?? Page();
        }

        DateTimeOffset? baseModified = DateTimeOffset.TryParse(BaseModified, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
        var assignee = string.IsNullOrWhiteSpace(NewAssignee) ? null : NewAssignee.Trim();
        var result = await _api.UpdateOrderAssigneeAsync(Id, new UpdateOrderAssigneeRequest { AssignedToUserId = assignee, BaseModifiedUtc = baseModified }, cancellationToken);
        if (result.IsSuccess)
        {
            if (assignee is null)
            {
                Flash = "This order is not assigned to anyone now.";
            }
            else
            {
                var names = await _api.GetStaffNamesAsync(cancellationToken);
                var name = names.IsSuccess ? names.Value!.FirstOrDefault(s => s.UserId == assignee)?.Name : null;
                Flash = name is null ? "Order assigned." : $"Order assigned to {name}.";
            }

            return RedirectToPage(new { id = Id }); // PRG
        }

        if (result.Status == System.Net.HttpStatusCode.Unauthorized)
        {
            return Fail(result)!;
        }

        ModelState.Clear();
        var reload = await LoadAsync(cancellationToken);
        if (result.Code == StaleUpdate.Code && Order?.AssignedToUserId == assignee)
        {
            StatusMessage = "Assignment saved."; // double submit: already assigned as asked
            return reload ?? Page();
        }

        AssignError = result.Code == StaleUpdate.Code
            ? "Someone changed this order while you were looking at it. The latest version is shown below. Pick the person again if you still need to."
            : StatusErrorFor(result);
        return reload ?? Page();
    }

    /// <summary>Typed code → plain copy; STALE_UPDATE gets its own explanation; unknown codes keep the Api's detail.</summary>
    public static string StatusErrorFor<T>(ApiResult<T> result)
    {
        if (result.Unreachable)
        {
            return result.Message;
        }

        if (result.Code == StaleUpdate.Code)
        {
            return StaleMessage;
        }

        var copy = OrderErrorMessages.For(result.Code);
        return result.Code is not null && copy != OrderErrorMessages.Fallback ? copy : result.Message;
    }

    /// <summary>Tap-to-send: the (editable) message text; prefilled per status.</summary>
    [BindProperty, MaxLength(1000)] public string? WhatsAppText { get; set; }

    [BindProperty] public bool OptIn { get; set; }

    /// <summary>Owner/Manager: the customer's automatic-message choice (null hides it).</summary>
    public WhatsAppConsentDto? Consent { get; private set; }

    /// <summary>Owner/Manager: automatic messages for this order (null hides the panel).</summary>
    public WhatsAppMessagePageDto? WhatsAppLog { get; private set; }

    /// <summary>wa.me digits, or null when the order's phone isn't a usable number.</summary>
    public string? WhatsAppDigits => WhatsAppLinks.ToWhatsAppDigits(Order?.CustomerPhone);

    public string DefaultWhatsAppText => Order is null ? string.Empty
        : WhatsAppLinks.Message(Order.Status, Order.CustomerName, User.BusinessName(), Order.DisplayRef);

    /// <summary>
    /// Audits "opened" (best effort: a failed audit never stops the message) then sends the browser to wa.me with the
    /// edited text. Nothing is sent by NeedleBase; the user presses Send in WhatsApp.
    /// </summary>
    public async Task<IActionResult> OnPostWhatsAppAsync(CancellationToken cancellationToken)
    {
        var order = await _api.GetOrderAsync(Id, cancellationToken);
        if (!order.IsSuccess)
        {
            return Fail(order) ?? await ReloadAsync(cancellationToken);
        }

        Order = order.Value;
        var text = string.IsNullOrWhiteSpace(WhatsAppText) ? DefaultWhatsAppText : WhatsAppText.Trim();
        var url = WhatsAppLinks.Url(Order!.CustomerPhone, text);
        if (url is null)
        {
            Order = null;
            ModelState.Clear();
            await LoadAsync(cancellationToken);
            StatusError = WhatsAppLinks.NoNumberMessage;
            return Page();
        }

        try
        {
            await _api.RecordWhatsAppOpenedAsync(Id, Order.Status, cancellationToken);
        }
        catch (Exception)
        {
            // audit is best effort
        }

        return Redirect(url);
    }

    /// <summary>Owner/Manager: turn automatic WhatsApp updates on/off for this order's customer.</summary>
    public async Task<IActionResult> OnPostConsentAsync(CancellationToken cancellationToken)
    {
        if (!User.CanSeeActivity())
        {
            ModelState.Clear();
            var denied = await LoadAsync(cancellationToken);
            StatusError = "Only the owner or a manager can change automatic updates.";
            return denied ?? Page();
        }

        var order = await _api.GetOrderAsync(Id, cancellationToken);
        if (!order.IsSuccess)
        {
            return Fail(order) ?? await ReloadAsync(cancellationToken);
        }

        var result = await _api.SetWhatsAppConsentAsync(order.Value!.CustomerId, OptIn, cancellationToken);
        if (result.IsSuccess)
        {
            Flash = OptIn ? "This customer will get automatic WhatsApp updates." : "Automatic WhatsApp updates are off for this customer.";
            return RedirectToPage(new { id = Id });
        }

        if (result.Status == System.Net.HttpStatusCode.Unauthorized)
        {
            return Fail(result)!;
        }

        ModelState.Clear();
        var reload = await LoadAsync(cancellationToken);
        StatusError = result.Message;
        return reload ?? Page();
    }

    private async Task<IActionResult> ReloadAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        return await LoadAsync(cancellationToken) ?? Page();
    }

    public const int HistorySize = 20;

    /// <summary>Latest activity on this order (Owner/Manager); null hides the panel.</summary>
    public PaginatedResponse<AuditEventDto>? History { get; private set; }

    /// <summary>Owner/Manager on an order that isn't collected/cancelled (the Api enforces the same).</summary>
    public bool CanEdit => Order is not null && User.CanEditOrders() && !OrderStatus.IsTerminal(Order.Status);

    /// <summary>Open order, and a Tailor only on their own or unassigned orders (the Api enforces the same).</summary>
    public bool CanChangeStatus => Order is not null && !OrderStatus.IsTerminal(Order.Status) &&
        NeedleBase.Application.Orders.OrderEditor.CanChangeStatus(User.Role(), User.UserId(), Order.AssignedToUserId);

    private async Task<IActionResult?> LoadAsync(CancellationToken cancellationToken)
    {
        var orderTask = _api.GetOrderAsync(Id, cancellationToken);
        var paymentsTask = User.CanSeePayments() ? _api.GetOrderPaymentsAsync(Id, cancellationToken) : null;
        var staffTask = _api.GetStaffNamesAsync(cancellationToken);
        var historyTask = User.CanSeeActivity()
            ? _api.ListActivityAsync(new AuditQuery { OrderId = Id, PageSize = HistorySize }, cancellationToken)
            : null;
        var waLogTask = User.CanSeeActivity() ? _api.ListWhatsAppMessagesAsync(Id, 1, cancellationToken) : null;
        var expensesTask = User.CanSeeExpenses()
            ? _api.GetExpensesAsync(new NeedleBase.Application.Contracts.Expenses.ExpenseListQuery
            {
                OrderId = Id, IncludeVoided = true, PageSize = ExpensePreviewCount
            }, cancellationToken)
            : null;
        await Task.WhenAll(new Task?[] { orderTask, staffTask, paymentsTask, historyTask, waLogTask, expensesTask }.OfType<Task>());

        var order = await orderTask;
        if (!order.IsSuccess)
        {
            if (order.Status == System.Net.HttpStatusCode.NotFound)
            {
                PageError = "This order wasn't found. It may have been deleted, or it belongs to another business.";
                CanRetry = false;
                return null;
            }

            return Fail(order);
        }

        Order = order.Value;
        BaseModified = Order!.DateModifiedUtc.ToString("o", CultureInfo.InvariantCulture);
        if (paymentsTask is not null && (await paymentsTask) is { IsSuccess: true } payments)
        {
            Payments = payments.Value; // 403 (role changed) simply hides the section
        }

        if (expensesTask is not null && (await expensesTask) is { IsSuccess: true } expenses)
        {
            Expenses = expenses.Value; // Starter (EXPENSES_NOT_ALLOWED) or a role change simply hides the card
            CanAddExpense = (await ExpenseWeb.StatusAsync(HttpContext, _api, cancellationToken))?.IsReadOnly != true;
        }

        if (historyTask is not null && (await historyTask) is { IsSuccess: true } history)
        {
            History = history.Value; // failure just hides the panel; the order itself loaded
        }

        if (waLogTask is not null && (await waLogTask) is { IsSuccess: true } waLog)
        {
            WhatsAppLog = waLog.Value;
        }

        if (User.CanSeeActivity() && (await _api.GetWhatsAppConsentAsync(Order.CustomerId, cancellationToken)) is { IsSuccess: true } consent)
        {
            Consent = consent.Value; // needs the customer id, so it follows the order call
        }

        var staff = await staffTask;
        if (staff.IsSuccess)
        {
            Staff = staff.Value!;
        }

        NewAssignee = Order.AssignedToUserId ?? string.Empty;
        if (Order.AssignedToUserId is { Length: > 0 } assignee)
        {
            AssigneeName = staff.IsSuccess
                ? staff.Value!.FirstOrDefault(s => s.UserId == assignee)?.Name ?? "Former team member"
                : "Assigned";
        }

        Loaded = true;
        return null;
    }
}
