using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Api.Http;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Payments;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;

namespace NeedleBase.Api.Controllers;

/// <summary>
/// Payments (Slice 5). Owner/Manager only: a tailor gets 403 PAYMENT_FORBIDDEN (checked in the service).
/// Other businesses' orders/payments read as not found (tenant filter, fail closed).
/// </summary>
[ApiController]
[Authorize(Policy = "RequireStaff")]
public sealed class PaymentsController : ControllerBase
{
    private readonly IPaymentService _payments;
    private readonly IAuditLog _audit;
    private readonly ICurrentUser _currentUser;

    public PaymentsController(IPaymentService payments, IAuditLog audit, ICurrentUser currentUser)
    {
        _payments = payments;
        _audit = audit;
        _currentUser = currentUser;
    }

    /// <summary>Money views are sensitive: one activity row per person per hour (not per page load).</summary>
    private Task LogViewAsync(string what) =>
        _audit.WriteAsync(new AuditEntry(AuditActions.PaymentsViewed, $"Viewed {what}"),
            throttleKey: $"{AuditActions.PaymentsViewed}:{_currentUser.BusinessId}:{_currentUser.UserId}", window: TimeSpan.FromHours(1));

    [HttpGet("api/payments")]
    [ProducesResponseType(typeof(PaginatedResponse<PaymentListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationDefaults.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _payments.ListAsync(page, pageSize, cancellationToken);
        if (result.IsSuccess)
        {
            await LogViewAsync("the payments list");
        }

        return result.IsSuccess ? Ok(result.Value) : ApiProblems.ForPayment(HttpContext, result.Error, result.Message);
    }

    [HttpGet("api/payments/{id}")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var result = await _payments.GetAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ApiProblems.ForPayment(HttpContext, result.Error, result.Message);
    }

    [HttpGet("api/orders/{orderId}/payments")]
    [ProducesResponseType(typeof(OrderPaymentsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ForOrder(string orderId, CancellationToken cancellationToken)
    {
        var result = await _payments.GetForOrderAsync(orderId, cancellationToken);
        if (result.IsSuccess)
        {
            await LogViewAsync("order payments");
        }

        return result.IsSuccess ? Ok(result.Value) : ApiProblems.ForPayment(HttpContext, result.Error, result.Message);
    }

    [HttpPost("api/orders/{orderId}/payments")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Record(string orderId, [FromBody] RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        request.Lenient = false; // entry rules always apply on REST
        var result = await _payments.RecordAsync(orderId, request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value)
            : ApiProblems.ForPayment(HttpContext, result.Error, result.Message);
    }

    [HttpPost("api/payments/{id}/void")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Void(string id, [FromBody] VoidPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await _payments.VoidAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ApiProblems.ForPayment(HttpContext, result.Error, result.Message);
    }
}
