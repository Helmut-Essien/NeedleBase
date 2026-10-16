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

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDetailDto>> Get(string id, CancellationToken cancellationToken)
    {
        var customer = await _customers.GetByIdAsync(id, cancellationToken);
        return customer is null ? NotFound() : Ok(customer);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDetailDto>> Create(
        [FromBody] CreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var (customer, error) = await _customers.CreateAsync(request, cancellationToken);
        if (error == "Customer id conflict.")
        {
            return Conflict(new { message = error });
        }

        if (error is not null || customer is null)
        {
            return BadRequest(new { message = error ?? "Could not create customer." });
        }

        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDetailDto>> Update(
        string id,
        [FromBody] UpdateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var (customer, error) = await _customers.UpdateAsync(id, request, cancellationToken);
        if (error == "Customer not found.")
        {
            return NotFound();
        }

        if (error is not null || customer is null)
        {
            return BadRequest(new { message = error ?? "Could not update customer." });
        }

        return Ok(customer);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var (success, error) = await _customers.DeleteAsync(id, cancellationToken);
        if (!success)
        {
            return NotFound(new { message = error });
        }

        return NoContent();
    }
}
