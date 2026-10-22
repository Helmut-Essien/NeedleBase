using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Api.Http;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Orders;

namespace NeedleBase.Api.Controllers;

[ApiController]
[Authorize(Policy = "RequireStaff")]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderService _orders;

    public OrdersController(IOrderService orders)
    {
        _orders = orders;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<OrderListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginatedResponse<OrderListItemDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationDefaults.DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery, StringLength(16)] string? sort = null,
        [FromQuery, StringLength(64)] string? assignedTo = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _orders.ListAsync(page, pageSize, search, status, sort, assignedTo, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailDto>> Get(string id, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(id, cancellationToken);
        return order is null ? ApiProblems.OrderNotFound(HttpContext) : Ok(order);
    }

    [HttpPost]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDetailDto>> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orders.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Order!.Id }, result.Order)
            : ApiProblems.ForOrder(HttpContext, result.Error);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDetailDto>> Update(
        string id,
        [FromBody] UpdateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orders.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Order) : ApiProblems.ForOrder(HttpContext, result.Error);
    }

    [HttpPatch("{id}/status")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDetailDto>> UpdateStatus(
        string id,
        [FromBody] UpdateOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orders.UpdateStatusAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Order) : ApiProblems.ForOrder(HttpContext, result.Error);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var error = await _orders.DeleteAsync(id, cancellationToken);
        return error == OrderError.None ? NoContent() : ApiProblems.ForOrder(HttpContext, error);
    }
}
