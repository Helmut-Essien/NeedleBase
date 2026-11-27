using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Expenses;
using NeedleBase.Application.Licensing;
using NeedleBase.Application.Reports;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Expenses;

/// <summary>
/// Record expense (expenses plan 7.3). The client id is made on GET and posted back, so a double submit saves one
/// expense (the Api returns the saved one). <c>?orderId=</c> prefills the order; success goes back where it came from.
/// </summary>
public class CreateModel : AppPageModel
{
    public const string ConflictMessage =
        "An expense with different details was already saved from this form. Check the expenses list before recording it again.";

    private readonly INeedleBaseApi _api;

    public CreateModel(INeedleBaseApi api)
    {
        _api = api;
    }

    /// <summary>Linked order (prefilled from <c>?orderId=</c> or picked from the list).</summary>
    [BindProperty(SupportsGet = true), MaxLength(FieldLimits.Id)] public string? OrderId { get; set; }

    /// <summary>Order search text for the picker (GET only).</summary>
    [BindProperty(SupportsGet = true, Name = "q"), MaxLength(100)] public string? OrderSearch { get; set; }

    [BindProperty, MaxLength(FieldLimits.Id)] public string? ClientId { get; set; }

    [BindProperty, MaxLength(20)] public string? Amount { get; set; }

    [BindProperty, MaxLength(FieldLimits.ExpenseCategory)] public string? Category { get; set; }

    /// <summary><c>yyyy-MM-dd</c> from the date input.</summary>
    [BindProperty, MaxLength(10)] public string? SpentOn { get; set; }

    [BindProperty] public string? Note { get; set; }

    /// <summary>Opened from an order: go back to it after saving.</summary>
    [BindProperty] public bool FromOrder { get; set; }

    [TempData] public string? Flash { get; set; }

    /// <summary>Clock for tests.</summary>
    public Func<DateTimeOffset> UtcNow { get; set; } = () => DateTimeOffset.UtcNow;

    public string? AmountError { get; private set; }
    public string? CategoryError { get; private set; }
    public string? DateError { get; private set; }
    public string? NoteError { get; private set; }
    public string? OrderError { get; private set; }
    public string? FormError { get; private set; }

    /// <summary>Why the form isn't offered (read-only shop); null shows the form.</summary>
    public string? BlockedMessage { get; private set; }
    public bool NeedsUpgrade { get; private set; }
    public string? OrderLabel { get; private set; }
    public IReadOnlyList<OrderListItemDto> Orders { get; private set; } = Array.Empty<OrderListItemDto>();

    public DateOnly Today => BusinessTime.Today(UtcNow());
    public string TodayKey => Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string EarliestKey => Expense.EarliestSpentOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public bool ShowForm => Loaded && BlockedMessage is null && !NeedsUpgrade;
    public bool HasOrder => !string.IsNullOrWhiteSpace(OrderId) && OrderLabel is not null;
    public string BackUrl => FromOrder && !string.IsNullOrWhiteSpace(OrderId)
        ? $"/Orders/Details/{Uri.EscapeDataString(OrderId)}"
        : "/Expenses";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        OrderId = string.IsNullOrWhiteSpace(OrderId) ? null : OrderId.Trim();
        FromOrder = OrderId is not null;
        ClientId = IdGenerator.NewId();
        SpentOn = TodayKey;
        return await PrepareAsync(cancellationToken) ?? Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        OrderId = string.IsNullOrWhiteSpace(OrderId) ? null : OrderId.Trim();
        if (!User.CanSeeExpenses())
        {
            return await PrepareAsync(cancellationToken) ?? Page();
        }

        if (string.IsNullOrWhiteSpace(ClientId) || ClientId.Length > FieldLimits.Id)
        {
            ClientId = IdGenerator.NewId();
        }

        var entry = Validate(out var amount, out var date);
        if (entry is null)
        {
            return await PrepareAsync(cancellationToken) ?? Page();
        }

        var result = await _api.RecordExpenseAsync(new RecordExpenseRequest
        {
            Id = ClientId,
            Amount = amount,
            Category = entry.Category,
            SpentOn = date,
            Note = entry.Note,
            OrderId = OrderId
        }, cancellationToken);
        if (result.IsSuccess)
        {
            Flash = ExpenseWeb.FlashRecorded;
            return FromOrder && OrderId is not null
                ? Redirect($"/Orders/Details/{Uri.EscapeDataString(OrderId)}")
                : Redirect($"/Expenses?month={ReportRules.Key(ReportRules.FirstOfMonth(date))}");
        }

        if (result.Status == System.Net.HttpStatusCode.Unauthorized)
        {
            return Fail(result)!;
        }

        switch (result.Code)
        {
            case RecordErrorMessages.ExpensesNotAllowed:
                NeedsUpgrade = true;
                break;
            case LicenseErrors.ReadOnly:
                FormError = LicenseErrors.ReadOnlyMessage;
                break;
            case RecordErrorMessages.ExpenseOrderInvalid:
                OrderError = result.Message;
                OrderId = null;
                FormError = "Check the order.";
                break;
            case RecordErrorMessages.ExpenseConflict:
                FormError = ConflictMessage;
                ClientId = IdGenerator.NewId();
                break;
            default:
                FormError = result.Message;
                break;
        }

        var prepared = await PrepareAsync(cancellationToken);
        return prepared ?? Page();
    }

    /// <summary>Same rules as the Api (strict): field messages plus one summary at the top.</summary>
    public ExpenseEntry? Validate(out decimal amount, out DateOnly date)
    {
        amount = 0;
        date = Today;
        var parsed = Orders_EditParse(Amount);
        if (parsed is null)
        {
            AmountError = "Enter the amount paid, e.g. 120 or 1,250.50.";
        }
        else if (MoneyRules.CheckAmount(parsed.Value) is { } amountProblem)
        {
            AmountError = amountProblem;
        }
        else
        {
            amount = parsed.Value;
        }

        if (!ExpenseCategory.IsValid(Category))
        {
            CategoryError = "Choose what the money was spent on.";
        }

        if (!DateOnly.TryParseExact(SpentOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            DateError = "Choose the date the money was paid.";
            date = Today;
        }
        else if (Expense.CheckSpentOn(date, Today) is { } dateProblem)
        {
            DateError = dateProblem;
        }

        if ((Note ?? string.Empty).Trim().Length > FieldLimits.Notes)
        {
            NoteError = $"Note must be at most {FieldLimits.Notes:N0} characters.";
        }

        if (AmountError is not null || CategoryError is not null || DateError is not null || NoteError is not null)
        {
            var fix = new List<string>();
            if (AmountError is not null) fix.Add("amount");
            if (CategoryError is not null) fix.Add("category");
            if (DateError is not null) fix.Add("date");
            if (NoteError is not null) fix.Add("note");
            FormError = $"Check the {string.Join(", ", fix)}.";
            return null;
        }

        var (error, detail, entry) = ExpenseRules.Check(amount, Category, date, Note, Today, lenient: false);
        if (error != RecordError.None || entry is null)
        {
            FormError = detail ?? "Check the details and try again.";
            return null;
        }

        return entry;
    }

    private static decimal? Orders_EditParse(string? text) => NeedleBase.Web.Pages.Orders.EditModel.ParseMoney(text);

    /// <summary>Role, plan and read-only checks, then the order label or picker list.</summary>
    private async Task<IActionResult?> PrepareAsync(CancellationToken cancellationToken)
    {
        if (!User.CanSeeExpenses())
        {
            PageError = ExpenseRules.ForbiddenMessage;
            CanRetry = false;
            return null;
        }

        var status = await ExpenseWeb.StatusAsync(HttpContext, _api, cancellationToken);
        if (status?.IsReadOnly == true)
        {
            BlockedMessage = LicenseErrors.ReadOnlyMessage;
        }
        else if (status is { CanReports: false })
        {
            NeedsUpgrade = true;
        }

        if (OrderId is not null)
        {
            var order = await _api.GetOrderAsync(OrderId, cancellationToken);
            if (order.IsSuccess)
            {
                OrderLabel = $"Order #{order.Value!.DisplayRef} · {order.Value.CustomerName}";
            }
            else if (order.Status == System.Net.HttpStatusCode.Unauthorized)
            {
                return Fail(order);
            }
            else
            {
                // Deleted or another shop's: don't link to something the Api will refuse.
                OrderError ??= "That order wasn't found, so the expense won't be linked to it.";
                OrderId = null;
            }
        }

        if (OrderId is null)
        {
            var search = string.IsNullOrWhiteSpace(OrderSearch) ? null : OrderSearch.Trim();
            var list = await _api.ListOrdersAsync(new OrderQuery(PageSize: 30, Search: search), cancellationToken);
            if (list.IsSuccess)
            {
                Orders = list.Value!.Data;
            }
        }

        Loaded = true;
        return null;
    }
}
