using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Dashboard;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Contracts.Audit;
using NeedleBase.Application.Contracts.WhatsApp;

namespace NeedleBase.Web.Services;

/// <summary>Typed Api calls used by the web pages (all data over HTTP; no DbContext in Web).</summary>
public interface INeedleBaseApi
{
    Task<ApiResult<DashboardSummaryDto>> GetDashboardAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<PaginatedResponse<OrderListItemDto>>> ListOrdersAsync(OrderQuery query, CancellationToken cancellationToken = default);

    Task<ApiResult<OrderDetailDto>> GetOrderAsync(string id, CancellationToken cancellationToken = default);

    Task<ApiResult<OrderDetailDto>> UpdateOrderAsync(string id, UpdateOrderRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<OrderDetailDto>> UpdateOrderStatusAsync(string id, UpdateOrderStatusRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<OrderPaymentsDto>> GetOrderPaymentsAsync(string orderId, CancellationToken cancellationToken = default);

    Task<ApiResult<PaginatedResponse<CustomerListItemDto>>> ListCustomersAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<ApiResult<PaginatedResponse<PaymentListItemDto>>> ListPaymentsAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<StaffNameDto>>> GetStaffNamesAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<PaginatedResponse<AuditEventDto>>> ListActivityAsync(AuditQuery query, CancellationToken cancellationToken = default);

    Task<ApiResult<StaffOverviewDto>> GetStaffAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<CreatedInvitationDto>> InviteAsync(CreateInvitationRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<bool>> RevokeInviteAsync(string invitationId, CancellationToken cancellationToken = default);

    Task<ApiResult<StaffMemberDto>> ChangeStaffRoleAsync(string userId, string role, CancellationToken cancellationToken = default);

    Task<ApiResult<StaffMemberDto>> SetStaffActiveAsync(string userId, bool active, CancellationToken cancellationToken = default);

    /// <summary>Owner: move a member's open orders to another active member, or unassign them (toUserId null).</summary>
    Task<ApiResult<ReassignOrdersResultDto>> ReassignOrdersAsync(string userId, string? toUserId, CancellationToken cancellationToken = default);

    /// <summary>New tokens for this browser (every other session is signed out).</summary>
    Task<ApiResult<AuthSessionPayload>> ChangePasswordAsync(NeedleBase.Application.Contracts.Auth.ChangePasswordRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<WhatsAppSettingsDto>> GetWhatsAppSettingsAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<WhatsAppSettingsDto>> UpdateWhatsAppSettingsAsync(UpdateWhatsAppSettingsRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<WhatsAppConsentDto>> GetWhatsAppConsentAsync(string customerId, CancellationToken cancellationToken = default);

    Task<ApiResult<WhatsAppConsentDto>> SetWhatsAppConsentAsync(string customerId, bool optedIn, CancellationToken cancellationToken = default);

    Task<ApiResult<WhatsAppMessagePageDto>> ListWhatsAppMessagesAsync(string? orderId, int page, CancellationToken cancellationToken = default);

    /// <summary>Tap-to-send audit ("opened", not "sent"); best effort.</summary>
    Task<ApiResult<bool>> RecordWhatsAppOpenedAsync(string orderId, string? status, CancellationToken cancellationToken = default);
}

/// <summary>Order list filters (server-side paging/filtering).</summary>
public sealed record OrderQuery(
    int Page = 1,
    int PageSize = PaginationDefaults.DefaultPageSize,
    string? Search = null,
    string? Status = null,
    string? Sort = null,
    string? AssignedTo = null);
