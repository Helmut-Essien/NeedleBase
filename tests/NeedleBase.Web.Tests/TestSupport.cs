using NeedleBase.Application.Contracts.Audit;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Dashboard;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Contracts.WhatsApp;
using NeedleBase.Web.Services;

namespace NeedleBase.Web.Tests;

/// <summary>In-memory INeedleBaseApi: records every call; each method answers from a settable delegate.</summary>
public sealed class FakeApi : INeedleBaseApi
{
    public ConcurrentQueue<string> Calls { get; } = new();
    public ConcurrentQueue<OrderQuery> OrderQueries { get; } = new();

    public Func<ApiResult<DashboardSummaryDto>> Dashboard { get; set; } = () => ApiResult<DashboardSummaryDto>.Ok(Data.Summary(withMoney: true));
    public Func<OrderQuery, ApiResult<PaginatedResponse<OrderListItemDto>>> Orders { get; set; } = q => ApiResult<PaginatedResponse<OrderListItemDto>>.Ok(Data.Page<OrderListItemDto>([]));
    public Func<string, ApiResult<OrderDetailDto>> Order { get; set; } = id => ApiResult<OrderDetailDto>.Ok(Data.Detail(id));
    public Func<string, UpdateOrderStatusRequest, ApiResult<OrderDetailDto>> UpdateStatus { get; set; } = (id, r) => ApiResult<OrderDetailDto>.Ok(Data.Detail(id, r.Status));
    public Func<ApiResult<OrderPaymentsDto>> OrderPayments { get; set; } = () => ApiResult<OrderPaymentsDto>.Ok(new OrderPaymentsDto("o1", "New", 100m, 40m, []));
    public Func<ApiResult<PaginatedResponse<PaymentListItemDto>>> Payments { get; set; } = () => ApiResult<PaginatedResponse<PaymentListItemDto>>.Ok(Data.Page<PaymentListItemDto>([]));
    public Func<ApiResult<PaginatedResponse<CustomerListItemDto>>> Customers { get; set; } = () => ApiResult<PaginatedResponse<CustomerListItemDto>>.Ok(Data.Page<CustomerListItemDto>([]));
    public Func<AuditQuery, ApiResult<PaginatedResponse<AuditEventDto>>> Activity { get; set; } = q => ApiResult<PaginatedResponse<AuditEventDto>>.Ok(Data.Page<AuditEventDto>([]));
    public Func<ApiResult<IReadOnlyList<StaffNameDto>>> Staff { get; set; } = () => ApiResult<IReadOnlyList<StaffNameDto>>.Ok([]);
    public UpdateOrderStatusRequest? LastUpdate { get; private set; }

    public Func<ApiResult<StaffOverviewDto>> StaffOverview { get; set; } = () => ApiResult<StaffOverviewDto>.Ok(new StaffOverviewDto([], [], true));
    public Func<CreateInvitationRequest, ApiResult<CreatedInvitationDto>>? Invite { get; set; }
    public Func<NeedleBase.Application.Contracts.Auth.ChangePasswordRequest, ApiResult<AuthSessionPayload>>? ChangePassword { get; set; }
    public Task<ApiResult<StaffOverviewDto>> GetStaffAsync(CancellationToken c = default) { Calls.Enqueue("staff"); return Task.FromResult(StaffOverview()); }
    public Task<ApiResult<CreatedInvitationDto>> InviteAsync(CreateInvitationRequest r, CancellationToken c = default) { Calls.Enqueue("invite"); return Task.FromResult(Invite?.Invoke(r) ?? throw new InvalidOperationException("Invite not stubbed")); }
    public Task<ApiResult<bool>> RevokeInviteAsync(string id, CancellationToken c = default) { Calls.Enqueue("revoke:" + id); return Task.FromResult(ApiResult<bool>.Ok(true)); }
    public Task<ApiResult<StaffMemberDto>> ChangeStaffRoleAsync(string userId, string role, CancellationToken c = default) { Calls.Enqueue($"role:{userId}:{role}"); throw new InvalidOperationException("Role not stubbed"); }
    public Task<ApiResult<StaffMemberDto>> SetStaffActiveAsync(string userId, bool active, CancellationToken c = default) { Calls.Enqueue($"active:{userId}:{active}"); throw new InvalidOperationException("Active not stubbed"); }
    public Func<string, string?, ApiResult<ReassignOrdersResultDto>>? Reassign { get; set; }
    public Task<ApiResult<ReassignOrdersResultDto>> ReassignOrdersAsync(string userId, string? toUserId, CancellationToken c = default) { Calls.Enqueue($"reassign:{userId}:{toUserId}"); return Task.FromResult(Reassign?.Invoke(userId, toUserId) ?? throw new InvalidOperationException("Reassign not stubbed")); }
    public Func<ApiResult<WhatsAppSettingsDto>> WhatsAppSettings { get; set; } = () => ApiResult<WhatsAppSettingsDto>.Ok(new WhatsAppSettingsDto(false, false, [], [], 2, null));
    public Func<UpdateWhatsAppSettingsRequest, ApiResult<WhatsAppSettingsDto>>? UpdateWhatsApp { get; set; }
    public Func<string, ApiResult<WhatsAppConsentDto>> Consent { get; set; } = id => ApiResult<WhatsAppConsentDto>.Ok(new WhatsAppConsentDto(id, false, null, null));
    public Func<ApiResult<WhatsAppMessagePageDto>> WhatsAppMessages { get; set; } = () => ApiResult<WhatsAppMessagePageDto>.Ok(new WhatsAppMessagePageDto([], 1, 50, false));
    public ConcurrentQueue<(string OrderId, string? Status)> Opened { get; } = new();
    public ConcurrentQueue<(string CustomerId, bool OptedIn)> ConsentChanges { get; } = new();
    public Task<ApiResult<WhatsAppSettingsDto>> GetWhatsAppSettingsAsync(CancellationToken c = default) { Calls.Enqueue("wa-settings"); return Task.FromResult(WhatsAppSettings()); }
    public Task<ApiResult<WhatsAppSettingsDto>> UpdateWhatsAppSettingsAsync(UpdateWhatsAppSettingsRequest r, CancellationToken c = default) { Calls.Enqueue("wa-settings-put"); return Task.FromResult(UpdateWhatsApp?.Invoke(r) ?? throw new InvalidOperationException("UpdateWhatsApp not stubbed")); }
    public Task<ApiResult<WhatsAppConsentDto>> GetWhatsAppConsentAsync(string id, CancellationToken c = default) { Calls.Enqueue("wa-consent"); return Task.FromResult(Consent(id)); }
    public Task<ApiResult<WhatsAppConsentDto>> SetWhatsAppConsentAsync(string id, bool optedIn, CancellationToken c = default) { Calls.Enqueue("wa-consent-put"); ConsentChanges.Enqueue((id, optedIn)); return Task.FromResult(ApiResult<WhatsAppConsentDto>.Ok(new WhatsAppConsentDto(id, optedIn, "staff", DateTimeOffset.UtcNow))); }
    public Task<ApiResult<WhatsAppMessagePageDto>> ListWhatsAppMessagesAsync(string? orderId, int page, CancellationToken c = default) { Calls.Enqueue("wa-messages"); return Task.FromResult(WhatsAppMessages()); }
    public Task<ApiResult<bool>> RecordWhatsAppOpenedAsync(string orderId, string? status, CancellationToken c = default) { Calls.Enqueue("wa-opened"); Opened.Enqueue((orderId, status)); return Task.FromResult(ApiResult<bool>.Ok(true)); }
    public Task<ApiResult<AuthSessionPayload>> ChangePasswordAsync(NeedleBase.Application.Contracts.Auth.ChangePasswordRequest r, CancellationToken c = default) { Calls.Enqueue("change-password"); return Task.FromResult(ChangePassword?.Invoke(r) ?? throw new InvalidOperationException("ChangePassword not stubbed")); }
    public Task<ApiResult<DashboardSummaryDto>> GetDashboardAsync(CancellationToken c = default) { Calls.Enqueue("dashboard"); return Task.FromResult(Dashboard()); }
    public Task<ApiResult<PaginatedResponse<OrderListItemDto>>> ListOrdersAsync(OrderQuery q, CancellationToken c = default) { Calls.Enqueue("orders"); OrderQueries.Enqueue(q); return Task.FromResult(Orders(q)); }
    public Task<ApiResult<OrderDetailDto>> GetOrderAsync(string id, CancellationToken c = default) { Calls.Enqueue("order"); return Task.FromResult(Order(id)); }
    public UpdateOrderRequest? LastEdit { get; private set; }
    public Func<string, UpdateOrderRequest, ApiResult<OrderDetailDto>>? UpdateOrder { get; set; }
    public Task<ApiResult<OrderDetailDto>> UpdateOrderAsync(string id, UpdateOrderRequest r, CancellationToken c = default) { Calls.Enqueue("edit"); LastEdit = r; return Task.FromResult(UpdateOrder?.Invoke(id, r) ?? ApiResult<OrderDetailDto>.Offline()); }
    public Task<ApiResult<OrderDetailDto>> UpdateOrderStatusAsync(string id, UpdateOrderStatusRequest r, CancellationToken c = default) { Calls.Enqueue("status"); LastUpdate = r; return Task.FromResult(UpdateStatus(id, r)); }
    public Task<ApiResult<OrderPaymentsDto>> GetOrderPaymentsAsync(string id, CancellationToken c = default) { Calls.Enqueue("orderPayments"); return Task.FromResult(OrderPayments()); }
    public Task<ApiResult<PaginatedResponse<CustomerListItemDto>>> ListCustomersAsync(string? s, int p, int ps, CancellationToken c = default) { Calls.Enqueue("customers"); return Task.FromResult(Customers()); }
    public Task<ApiResult<PaginatedResponse<PaymentListItemDto>>> ListPaymentsAsync(int p, int ps, CancellationToken c = default) { Calls.Enqueue("payments"); return Task.FromResult(Payments()); }
    public Task<ApiResult<IReadOnlyList<StaffNameDto>>> GetStaffNamesAsync(CancellationToken c = default) { Calls.Enqueue("staff"); return Task.FromResult(Staff()); }
    public AuditQuery? LastActivityQuery { get; private set; }
    public Task<ApiResult<PaginatedResponse<AuditEventDto>>> ListActivityAsync(AuditQuery q, CancellationToken c = default) { Calls.Enqueue("activity"); LastActivityQuery = q; return Task.FromResult(Activity(q)); }
}

public static class Data
{
    public static readonly DateTimeOffset Modified = new(2026, 10, 20, 9, 30, 0, TimeSpan.Zero);

    public static DashboardSummaryDto Summary(bool withMoney) =>
        new(3, 1, 2, 0, [new StatusCountDto("New", 3)], withMoney ? new DashboardMoneyDto(250m, 2, 0m, 50m) : null, new DateOnly(2026, 10, 22));

    public static PaginatedResponse<T> Page<T>(IReadOnlyList<T> items, int total = -1, int page = 1, int size = 20) =>
        PaginatedResponse<T>.Create(items, total < 0 ? items.Count : total, page, size);

    public static OrderListItemDto Item(string id, string status = "New", string? assignee = null) =>
        new(id, "ORD-" + id, "c1", "Ama Mensah", status, new DateOnly(2026, 10, 25), 100m, Modified, false, assignee);

    public static OrderDetailDto Detail(string id, string status = "New", string? assignee = null) =>
        new(id, "b1", "ORD-1", "c1", "Ama Mensah", "+233245550123", status, null, null, assignee, 100m, null, Modified, Modified, false, []);

    public static ApiResult<T> Problem<T>(HttpStatusCode status, string? code = null, string? detail = null) =>
        ApiResult<T>.Fail(new ApiProblem((int)status, code, null, detail, new Dictionary<string, string[]>()));
}

internal static class Pages
{
    public static T With<T>(T page, string? role, string path = "/x", string query = "") where T : PageModel
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "u1"), new(ClaimTypes.Name, "a@b.test") };
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies")) };
        http.Request.Path = path;
        http.Request.QueryString = new QueryString(query);
        page.PageContext = new PageContext(new Microsoft.AspNetCore.Mvc.ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.RazorPages.CompiledPageActionDescriptor()));
        page.TempData = new TempDataDictionary(http, new NullTempDataProvider());
        return page;
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
