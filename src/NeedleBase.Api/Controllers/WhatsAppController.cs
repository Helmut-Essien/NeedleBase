using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using NeedleBase.Api.Http;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Contracts.WhatsApp;
using NeedleBase.Infrastructure.Persistence;
using NeedleBase.Infrastructure.WhatsApp;

namespace NeedleBase.Api.Controllers;

/// <summary>
/// WhatsApp: settings (Owner/Manager), customer consent (Owner/Manager change it; any member can read it), the
/// automatic message log (Owner/Manager), the tap-to-send audit ping (any member) and Meta's webhook (anonymous,
/// signature-checked).
/// </summary>
[ApiController]
[Route("api/whatsapp")]
public sealed class WhatsAppController : ControllerBase
{
    private readonly WhatsAppService _service;
    private readonly ICurrentUser _user;

    public WhatsAppController(WhatsAppService service, ICurrentUser user)
    {
        _service = service;
        _user = user;
    }

    [HttpGet("settings")]
    [Authorize(Policy = "RequireManagerOrOwner")]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(_user.BusinessId) ? ApiProblems.SessionInvalid(HttpContext) : Ok(await _service.GetSettingsAsync(cancellationToken));

    [HttpPut("settings")]
    [Authorize(Policy = "RequireManagerOrOwner")]
    public async Task<IActionResult> PutSettings([FromBody] UpdateWhatsAppSettingsRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_user.BusinessId))
        {
            return ApiProblems.SessionInvalid(HttpContext);
        }

        var result = await _service.UpdateSettingsAsync(request, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Problem(result.ErrorCode);
    }

    [HttpGet("customers/{customerId}/consent")]
    [Authorize(Policy = "RequireStaff")]
    public async Task<IActionResult> GetConsent(string customerId, CancellationToken cancellationToken)
    {
        var result = await _service.GetConsentAsync(customerId, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Problem(result.ErrorCode);
    }

    [HttpPut("customers/{customerId}/consent")]
    [Authorize(Policy = "RequireManagerOrOwner")]
    public async Task<IActionResult> SetConsent(string customerId, [FromBody] SetWhatsAppConsentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_user.BusinessId))
        {
            return ApiProblems.SessionInvalid(HttpContext);
        }

        var result = await _service.SetConsentAsync(customerId, request.OptedIn, request.ChangedAtUtc, request.DeviceNowUtc, cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Problem(result.ErrorCode);
    }

    [HttpGet("messages")]
    [Authorize(Policy = "RequireManagerOrOwner")]
    public async Task<IActionResult> Messages([FromQuery] string? orderId, [FromQuery] int page = 1, CancellationToken cancellationToken = default) =>
        string.IsNullOrEmpty(_user.BusinessId)
            ? ApiProblems.SessionInvalid(HttpContext)
            : Ok(await _service.GetMessagesAsync(orderId, page, cancellationToken));

    [HttpPost("orders/{orderId}/opened")]
    [Authorize(Policy = "RequireStaff")]
    public async Task<IActionResult> Opened(string orderId, [FromBody] WhatsAppOpenedRequest? request, CancellationToken cancellationToken) =>
        await _service.RecordOpenedAsync(orderId, request?.Status, cancellationToken) ? NoContent() : ApiProblems.OrderNotFound(HttpContext);

    /// <summary>Meta webhook verification (GET with hub.mode, hub.verify_token, hub.challenge).</summary>
    [HttpGet("webhook")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public IActionResult VerifyWebhook([FromServices] IOptionsMonitor<WhatsAppOptions> options,
        [FromQuery(Name = "hub.mode")] string? mode, [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var answer = WhatsAppWebhook.Verify(options.CurrentValue, mode, token, challenge);
        return answer is null ? StatusCode(StatusCodes.Status403Forbidden) : Content(answer, "text/plain");
    }

    /// <summary>Meta webhook events (delivery statuses, replies). Body signature checked with the app secret.</summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [RequestSizeLimit(WhatsAppWebhook.MaxBodyBytes)]
    public async Task<IActionResult> Webhook([FromServices] IOptionsMonitor<WhatsAppOptions> options, [FromServices] ApplicationDbContext db,
        [FromServices] IAuditLog audit, [FromServices] IClock clock, [FromServices] ILogger<WhatsAppController> logger, CancellationToken cancellationToken)
    {
        var o = options.CurrentValue;
        if (!o.WebhookConfigured)
        {
            return NotFound();
        }

        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > WhatsAppWebhook.MaxBodyBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var body = buffer.ToArray();
        if (!WhatsAppWebhook.SignatureValid(o.AppSecret, body, Request.Headers["X-Hub-Signature-256"]))
        {
            return Unauthorized();
        }

        WhatsAppWebhook.Parsed parsed;
        try
        {
            parsed = WhatsAppWebhook.Parse(body);
        }
        catch (System.Text.Json.JsonException)
        {
            return Ok(); // signed but not JSON we understand: acknowledge so Meta doesn't retry forever
        }

        try
        {
            await WhatsAppWebhook.ApplyAsync(db, audit, parsed, clock.UtcNow, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "WhatsApp webhook update failed; Meta will retry");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok();
    }

    private ObjectResult Problem(string? code) => code switch
    {
        WhatsAppErrorCodes.NotConfigured => ApiProblems.Create(HttpContext, StatusCodes.Status409Conflict, code,
            "WhatsApp isn't set up on the server", "Automatic WhatsApp messages need the server's Meta settings. You can still tap to send."),
        WhatsAppErrorCodes.NoTemplate => ApiProblems.Create(HttpContext, StatusCodes.Status400BadRequest, code,
            "No WhatsApp template for that status", "Only statuses with an approved template can send automatic messages."),
        _ => ApiProblems.CustomerNotFound(HttpContext)
    };
}
