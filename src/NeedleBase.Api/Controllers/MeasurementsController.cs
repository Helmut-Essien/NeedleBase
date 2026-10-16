using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Measurements;

namespace NeedleBase.Api.Controllers;

[ApiController]
[Authorize(Policy = "RequireStaff")]
[Route("api")]
public sealed class MeasurementsController : ControllerBase
{
    private readonly IMeasurementService _measurements;

    public MeasurementsController(IMeasurementService measurements)
    {
        _measurements = measurements;
    }

    [HttpGet("customers/{customerId}/measurements")]
    [ProducesResponseType(typeof(PaginatedResponse<MeasurementListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaginatedResponse<MeasurementListItemDto>>> ListForCustomer(
        string customerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationDefaults.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var (pageResult, error) = await _measurements.ListForCustomerAsync(
            customerId,
            page,
            pageSize,
            cancellationToken);
        if (error == "Customer not found.")
        {
            return NotFound(new { message = error });
        }

        return Ok(pageResult);
    }

    [HttpGet("measurements/{id}")]
    [ProducesResponseType(typeof(MeasurementDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MeasurementDetailDto>> Get(string id, CancellationToken cancellationToken)
    {
        var measurement = await _measurements.GetByIdAsync(id, cancellationToken);
        return measurement is null ? NotFound() : Ok(measurement);
    }

    [HttpPost("customers/{customerId}/measurements")]
    [ProducesResponseType(typeof(MeasurementDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MeasurementDetailDto>> Create(
        string customerId,
        [FromBody] CreateMeasurementRequest request,
        CancellationToken cancellationToken)
    {
        var (measurement, error) = await _measurements.CreateAsync(customerId, request, cancellationToken);
        if (error == "Customer not found.")
        {
            return NotFound(new { message = error });
        }

        if (error == "Measurement id conflict.")
        {
            return Conflict(new { message = error });
        }

        if (error is not null || measurement is null)
        {
            return BadRequest(new { message = error ?? "Could not save measurement." });
        }

        return CreatedAtAction(nameof(Get), new { id = measurement.Id }, measurement);
    }

    [HttpPut("measurements/{id}")]
    [ProducesResponseType(typeof(MeasurementDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MeasurementDetailDto>> Update(
        string id,
        [FromBody] UpdateMeasurementRequest request,
        CancellationToken cancellationToken)
    {
        var (measurement, error) = await _measurements.UpdateAsync(id, request, cancellationToken);
        if (error == "Measurement not found.")
        {
            return NotFound();
        }

        if (error is not null || measurement is null)
        {
            return BadRequest(new { message = error ?? "Could not update measurement." });
        }

        return Ok(measurement);
    }

    [HttpDelete("measurements/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var (success, error) = await _measurements.DeleteAsync(id, cancellationToken);
        if (!success)
        {
            return NotFound(new { message = error });
        }

        return NoContent();
    }
}
