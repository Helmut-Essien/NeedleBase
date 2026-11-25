using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Api.Http;
using NeedleBase.Application.Abstractions;
using NeedleBase.Infrastructure.Licensing;

namespace NeedleBase.Api.Controllers;

/// <summary>
/// Owner: download a JSON copy of the shop's Business Data. Allowed for paid / keyless / complimentary
/// shops, including while the licence is read-only. Free phone-only shops use "Save a copy" on the phone.
/// </summary>
[ApiController]
[Route("api/export")]
[Authorize(Policy = "RequireOwner")]
[AllowWhenReadOnly]
public sealed class ExportController : ControllerBase
{
    private readonly BusinessExportService _export;
    private readonly ICurrentUser _user;

    public ExportController(BusinessExportService export, ICurrentUser user)
    {
        _export = export;
        _user = user;
    }

    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_user.BusinessId))
        {
            return ApiProblems.SessionInvalid(HttpContext);
        }

        var (json, stem, error) = await _export.BuildAsync(_user.BusinessId, cancellationToken);
        if (json is null)
        {
            return error switch
            {
                "EXPORT_NOT_ALLOWED" => ApiProblems.Create(
                    HttpContext,
                    StatusCodes.Status403Forbidden,
                    "EXPORT_NOT_ALLOWED",
                    "Export isn't available on this plan.",
                    "Export isn't available on this plan."),
                _ => ApiProblems.BusinessNotFound(HttpContext)
            };
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        return File(bytes, "application/json", $"{stem}.json");
    }
}
