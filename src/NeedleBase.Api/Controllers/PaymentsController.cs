using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Api.Http;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Payments;

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

    public PaymentsController(IPaymentService payments)
    {
        _payments = payments;
    }

    [HttpGet("api/payments")]
    [ProducesResponseType(typeof(PaginatedResponse<PaymentListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationDefaults.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _payments.ListAsync(page, pageSize, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ApiProblems.ForPayment(HttpContext, result.Error);
    }

    [HttpGet("api/payments/{id}")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var result = await _payments.GetAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ApiProblems.ForPayment(HttpContext, result.Error);
    }

    [HttpGet("api/orders/{orderId}/payments")]
    [ProducesResponseType(typeof(OrderPaymentsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ForOrder(string orderId, CancellationToken cancellationToken)
    {
        var result = await _payments.GetForOrderAsync(orderId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ApiProblems.ForPayment(HttpContext, result.Error);
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
            : ApiProblems.ForPayment(HttpContext, result.Error);
    }

    [HttpPost("api/payments/{id}/void")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Void(string id, [FromBody] VoidPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await _payments.VoidAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ApiProblems.ForPayment(HttpContext, result.Error);
    }
}
