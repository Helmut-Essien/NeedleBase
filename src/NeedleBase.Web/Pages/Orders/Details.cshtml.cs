using NeedleBase.Application.Contracts.Audit;
using NeedleBase.Application.Common;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
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
    public string? AssigneeName { get; private set; }
    public string? StatusMessage { get; private set; }
    public string? StatusError { get; private set; }

    [TempData] public string? Flash { get; set; }

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
        await Task.WhenAll(new Task?[] { orderTask, staffTask, paymentsTask, historyTask, waLogTask }.OfType<Task>());

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
