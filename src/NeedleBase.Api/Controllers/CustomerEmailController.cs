using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Api.Http;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Infrastructure.Email;

namespace NeedleBase.Api.Controllers;

/// <summary>Transactional customer email opt-in (order status + payment receipts). Separate from WhatsApp consent.</summary>
[ApiController]
[Route("api/customers")]
public sealed class CustomerEmailController : ControllerBase
{
    private readonly CustomerEmailService _service;
    private readonly ICurrentUser _user;

    public CustomerEmailController(CustomerEmailService service, ICurrentUser user)
    {
        _service = service;
        _user = user;
    }

    [HttpGet("{customerId}/email-consent")]
    [Authorize(Policy = "RequireStaff")]
    public async Task<IActionResult> GetConsent(string customerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_user.BusinessId))
        {
            return ApiProblems.SessionInvalid(HttpContext);
        }

        var (value, error) = await _service.GetConsentAsync(customerId, cancellationToken);
        return error is null ? Ok(value) : NotFound();
    }

    [HttpPut("{customerId}/email-consent")]
    [AllowWhenReadOnly]
    [Authorize(Policy = "RequireManagerOrOwner")]
    public async Task<IActionResult> SetConsent(
        string customerId, [FromBody] SetCustomerEmailConsentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_user.BusinessId))
        {
            return ApiProblems.SessionInvalid(HttpContext);
        }

        var (value, error) = await _service.SetConsentAsync(
            customerId, request.OptedIn, request.ChangedAtUtc, request.DeviceNowUtc, cancellationToken);
        return error is null ? Ok(value) : NotFound();
    }
}
