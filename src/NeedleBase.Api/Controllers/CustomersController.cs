using NeedleBase.Api.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;

namespace NeedleBase.Api.Controllers;

[ApiController]
[Authorize(Policy = "RequireStaff")]
[Route("api/customers")]
public sealed class CustomersController : ControllerBase
{
    private readonly ICustomerService _customers;

    public CustomersController(ICustomerService customers)
    {
        _customers = customers;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResponse<CustomerListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginatedResponse<CustomerListItemDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationDefaults.DefaultPageSize,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _customers.ListAsync(page, pageSize, search, cancellationToken);
        return Ok(result);
    }

    /// <summary>Customers with the same number (any formatting), for a non-blocking duplicate warning.</summary>
    [HttpGet("phone-matches")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CustomerListItemDto>>> PhoneMatches(
        [FromQuery] string? phone,
        [FromQuery] string? excludeId,
        CancellationToken cancellationToken)
    {
        return Ok(await _customers.FindByPhoneAsync(phone, excludeId, cancellationToken));
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDetailDto>> Get(string id, CancellationToken cancellationToken)
    {
        var customer = await _customers.GetByIdAsync(id, cancellationToken);
        return customer is null ? ApiProblems.CustomerNotFound(HttpContext) : Ok(customer);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDetailDto>> Create(
        [FromBody] CreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _customers.CreateAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return ApiProblems.ForRecord(HttpContext, result.Error, "CUSTOMER_SAVE_FAILED", "customer");
        }

        var customer = result.Value!;

        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDetailDto>> Update(
        string id,
        [FromBody] UpdateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _customers.UpdateAsync(id, request, cancellationToken);
        if (!result.IsSuccess)
        {
            return ApiProblems.ForRecord(HttpContext, result.Error, "CUSTOMER_SAVE_FAILED", "customer");
        }

        var customer = result.Value!;

        return Ok(customer);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        return await _customers.DeleteAsync(id, cancellationToken) switch
        {
            CustomerDeleteResult.Deleted => NoContent(),
            CustomerDeleteResult.HasOrders => ApiProblems.CustomerHasOrders(HttpContext),
            _ => ApiProblems.CustomerNotFound(HttpContext)
        };
    }
}
