using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NeedleBase.Api.Http;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Contracts.Reports;
using NeedleBase.Application.Reports;
using NeedleBase.Infrastructure.Reports;

namespace NeedleBase.Api.Controllers;

/// <summary>
/// Profit and monthly reports (plan step 7b). Owner/Manager only (tailor → 403 REPORTS_FORBIDDEN); Workshop,
/// Production, complimentary and keyless shops (Starter / trial / unknown plan → 403 REPORTS_NOT_ALLOWED).
/// GET only, so it keeps working while a shop is read-only.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireStaff")]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly MonthlyReportService _reports;
    private readonly ICurrentUser _user;
    private readonly IAuditLog _audit;

    public ReportsController(MonthlyReportService reports, ICurrentUser user, IAuditLog audit)
    {
        _reports = reports;
        _user = user;
        _audit = audit;
    }

    /// <param name="month">Accra calendar month as <c>yyyy-MM</c>; blank = this month.</param>
    /// <param name="cancellationToken">Request aborted.</param>
    [HttpGet("monthly")]
    [ProducesResponseType(typeof(MonthlyReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Monthly([FromQuery] string? month, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_user.BusinessId))
        {
            return ApiProblems.SessionInvalid(HttpContext);
        }

        var (report, error) = await _reports.GetAsync(_user.BusinessId, _user.Role, month, cancellationToken);
        if (report is not null)
        {
            // Money view: same throttled activity row as the payments list (one per person per hour).
            await _audit.WriteAsync(new AuditEntry(AuditActions.PaymentsViewed, "Viewed the monthly report"),
                throttleKey: $"{AuditActions.PaymentsViewed}:{_user.BusinessId}:{_user.UserId}", window: TimeSpan.FromHours(1),
                cancellationToken: cancellationToken);
            return Ok(report);
        }

        return error switch
        {
            ReportRules.ForbiddenCode => ApiProblems.Create(HttpContext, StatusCodes.Status403Forbidden,
                ReportRules.ForbiddenCode, "No access to reports", ReportRules.ForbiddenMessage),
            ReportRules.NotAllowedCode => ApiProblems.Create(HttpContext, StatusCodes.Status403Forbidden,
                ReportRules.NotAllowedCode, "Reports aren't on this plan", ReportRules.NotAllowedMessage),
            ReportRules.MonthInvalidCode => ApiProblems.Create(HttpContext, StatusCodes.Status400BadRequest,
                ReportRules.MonthInvalidCode, "Month not available", ReportRules.MonthInvalidMessage),
            _ => ApiProblems.BusinessNotFound(HttpContext)
        };
    }
}
