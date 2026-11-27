using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Api.Http;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Expenses;
using NeedleBase.Application.Expenses;

namespace NeedleBase.Api.Controllers;

/// <summary>
/// Expenses (plans step 7b-2). Workshop, Production, complimentary and keyless shops (Starter / trial / unknown plan
/// → 403 EXPENSES_NOT_ALLOWED); Owner/Manager only (tailor → 403 EXPENSES_FORBIDDEN). Checked in the service.
/// Other shops' expenses read as not found. Writes are blocked while the shop is read-only (SubscriptionWriteGuard);
/// reads keep working.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireStaff")]
public sealed class ExpensesController : ControllerBase
{
    private readonly IExpenseService _expenses;
    private readonly IAuditLog _audit;
    private readonly ICurrentUser _currentUser;

    public ExpensesController(IExpenseService expenses, IAuditLog audit, ICurrentUser currentUser)
    {
        _expenses = expenses;
        _audit = audit;
        _currentUser = currentUser;
    }

    [HttpGet("api/expenses")]
    [ProducesResponseType(typeof(ExpenseListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? month,
        [FromQuery] string? category,
        [FromQuery] string? orderId,
        [FromQuery] bool includeVoided = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationDefaults.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _expenses.ListAsync(new ExpenseListQuery
        {
            Month = month, Category = category, OrderId = orderId, IncludeVoided = includeVoided, Page = page, PageSize = pageSize
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            return ApiProblems.ForExpense(HttpContext, result.Error, result.Message);
        }

        // Money views are sensitive: one activity row per person per hour (not per page load).
        await _audit.WriteAsync(new AuditEntry(AuditActions.ExpensesViewed, "Viewed expenses"),
            throttleKey: $"{AuditActions.ExpensesViewed}:{_currentUser.BusinessId}:{_currentUser.UserId}", window: TimeSpan.FromHours(1),
            cancellationToken: cancellationToken);
        return Ok(result.Value);
    }

    [HttpGet("api/expenses/{id}")]
    [ProducesResponseType(typeof(ExpenseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var result = await _expenses.GetAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ApiProblems.ForExpense(HttpContext, result.Error, result.Message);
    }

    /// <summary>Creates an expense; 201 when new, 200 when the same client id and content were already saved.</summary>
    [HttpPost("api/expenses")]
    [ProducesResponseType(typeof(ExpenseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ExpenseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Record([FromBody] RecordExpenseRequest request, CancellationToken cancellationToken)
    {
        request.Lenient = false; // entry rules always apply on REST
        var result = await _expenses.RecordAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return ApiProblems.ForExpense(HttpContext, result.Error, result.Message);
        }

        var saved = result.Value!;
        return saved.Created
            ? CreatedAtAction(nameof(Get), new { id = saved.Expense.Id }, saved.Expense)
            : Ok(saved.Expense);
    }

    /// <summary>Voids an expense (one-way). Voiding an already-voided expense returns it unchanged (first reason wins).</summary>
    [HttpPost("api/expenses/{id}/void")]
    [ProducesResponseType(typeof(ExpenseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Void(string id, [FromBody] VoidExpenseRequest request, CancellationToken cancellationToken)
    {
        request.Lenient = false;
        var result = await _expenses.VoidAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ApiProblems.ForExpense(HttpContext, result.Error, result.Message);
    }
}
