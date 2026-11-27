using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Expenses;
using NeedleBase.Application.Licensing;
using NeedleBase.Domain.Common;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Pages.Expenses;

/// <summary>
/// One expense (expenses plan 7.4): all fields, linked order, who recorded it and, for a live expense on a shop
/// that isn't read-only, the Void form (reason ≥ 3 characters + confirm box; STALE_UPDATE shows the fresh copy).
/// </summary>
public class DetailsModel : AppPageModel
{
    public const string ConfirmMessage = "Tick the box to confirm.";
    public const string StaleMessage =
        "Someone changed this expense while you were looking at it. The latest version is shown below.";

    private readonly INeedleBaseApi _api;

    public DetailsModel(INeedleBaseApi api)
    {
        _api = api;
    }

    [BindProperty(SupportsGet = true)] public string Id { get; set; } = string.Empty;

    [BindProperty, MaxLength(FieldLimits.Notes)] public string? VoidReason { get; set; }

    /// <summary>"I understand this stays in the records".</summary>
    [BindProperty] public bool ConfirmVoid { get; set; }

    /// <summary>Round-tripped "o" timestamp of the version the user saw.</summary>
    [BindProperty, MaxLength(64)] public string? BaseModified { get; set; }

    [TempData] public string? Flash { get; set; }

    public ExpenseDto? Expense { get; private set; }
    public IReadOnlyList<StaffNameDto> Staff { get; private set; } = Array.Empty<StaffNameDto>();
    public bool IsReadOnly { get; private set; }
    public bool NeedsUpgrade { get; private set; }
    public string? FlashMessage { get; private set; }
    public string? VoidError { get; private set; }
    public string? ConfirmError { get; private set; }

    public bool CanVoid => Expense is { IsVoided: false } && !IsReadOnly;
    public string? OrderLine => Expense is null ? null : ExpenseWeb.OrderLine(Expense);
    public string RecordedText => Expense is null ? string.Empty
        : $"{ExpenseWeb.Who(Expense.RecordedByUserId, Staff)} · {Money.DateTime(Expense.CreatedAtUtc)}";
    public string? VoidedText => Expense is { IsVoided: true } e
        ? $"{(e.VoidedByUserId is null ? "Voided" : "Voided by " + ExpenseWeb.Who(e.VoidedByUserId, Staff))}{(e.VoidedAtUtc is { } at ? " · " + Money.DateTime(at) : "")}"
        : null;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        FlashMessage = Flash;
        return await LoadAsync(cancellationToken) ?? Page();
    }

    public async Task<IActionResult> OnPostVoidAsync(CancellationToken cancellationToken)
    {
        if (!User.CanSeeExpenses())
        {
            return await LoadAsync(cancellationToken) ?? Page();
        }

        VoidError = ExpenseRules.CheckVoidReason(VoidReason, lenient: false);
        ConfirmError = ConfirmVoid ? null : ConfirmMessage;
        if (VoidError is not null || ConfirmError is not null)
        {
            return await LoadAsync(cancellationToken) ?? Page();
        }

        DateTimeOffset? baseModified = DateTimeOffset.TryParse(BaseModified, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
        var result = await _api.VoidExpenseAsync(Id, new VoidExpenseRequest { Reason = VoidReason!.Trim(), BaseModifiedUtc = baseModified }, cancellationToken);
        if (result.IsSuccess)
        {
            Flash = ExpenseWeb.FlashVoided;
            return RedirectToPage(new { id = Id }); // PRG: a refresh never re-posts
        }

        if (result.Status == System.Net.HttpStatusCode.Unauthorized)
        {
            return Fail(result)!;
        }

        ModelState.Clear(); // the page re-renders from the fresh copy
        var reload = await LoadAsync(cancellationToken);
        if (Expense is { IsVoided: true })
        {
            FlashMessage = "This expense is voided."; // double submit or someone else voided it first
            return reload ?? Page();
        }

        VoidError = result.Code switch
        {
            StaleUpdate.Code => StaleMessage,
            LicenseErrors.ReadOnly => LicenseErrors.ReadOnlyMessage,
            _ => result.Message
        };
        return reload ?? Page();
    }

    private async Task<IActionResult?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!User.CanSeeExpenses())
        {
            PageError = ExpenseRules.ForbiddenMessage;
            CanRetry = false;
            return null;
        }

        var expenseTask = _api.GetExpenseAsync(Id, cancellationToken);
        var staffTask = _api.GetStaffNamesAsync(cancellationToken);
        await Task.WhenAll(expenseTask, staffTask);
        var expense = await expenseTask;
        if (!expense.IsSuccess)
        {
            if (expense.Code == RecordErrorMessages.ExpensesNotAllowed)
            {
                NeedsUpgrade = true;
                return null;
            }

            if (expense.Status == System.Net.HttpStatusCode.NotFound)
            {
                PageError = "This expense wasn't found. It may belong to another business.";
                CanRetry = false;
                return null;
            }

            return Fail(expense);
        }

        Expense = expense.Value;
        BaseModified = Expense!.DateModifiedUtc.ToString("o", CultureInfo.InvariantCulture);
        if ((await staffTask) is { IsSuccess: true } staff)
        {
            Staff = staff.Value!;
        }

        IsReadOnly = (await ExpenseWeb.StatusAsync(HttpContext, _api, cancellationToken))?.IsReadOnly == true;
        Loaded = true;
        return null;
    }
}
