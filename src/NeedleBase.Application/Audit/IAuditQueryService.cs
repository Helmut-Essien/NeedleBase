using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Audit;

namespace NeedleBase.Application.Audit;

/// <summary>Read side of the audit trail (tenant-filtered; role rules applied inside).</summary>
public interface IAuditQueryService
{
    Task<PaginatedResponse<AuditEventDto>> ListAsync(AuditQuery query, CancellationToken cancellationToken = default);
}
