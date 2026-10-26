using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Dashboard;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Contracts.WhatsApp;
using NeedleBase.Application.Contracts.Audit;

namespace NeedleBase.Web.Services;

public sealed class NeedleBaseApi : INeedleBaseApi
{
    public const string ClientName = "NeedleBaseApi";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<NeedleBaseApi> _logger;

    public NeedleBaseApi(IHttpClientFactory factory, ILogger<NeedleBaseApi> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public Task<ApiResult<DashboardSummaryDto>> GetDashboardAsync(CancellationToken cancellationToken = default) =>
        SendAsync<DashboardSummaryDto>(HttpMethod.Get, "api/dashboard/summary", null, cancellationToken);

    public Task<ApiResult<PaginatedResponse<OrderListItemDto>>> ListOrdersAsync(OrderQuery query, CancellationToken cancellationToken = default)
    {
        var url = new StringBuilder("api/orders?page=").Append(Math.Max(1, query.Page))
            .Append("&pageSize=").Append(Math.Clamp(query.PageSize, 1, PaginationDefaults.MaxPageSize));
        Append(url, "search", query.Search);
        Append(url, "status", query.Status);
        Append(url, "sort", query.Sort);
        Append(url, "assignedTo", query.AssignedTo);
        return SendAsync<PaginatedResponse<OrderListItemDto>>(HttpMethod.Get, url.ToString(), null, cancellationToken);
    }

    public Task<ApiResult<OrderDetailDto>> GetOrderAsync(string id, CancellationToken cancellationToken = default) =>
        SendAsync<OrderDetailDto>(HttpMethod.Get, $"api/orders/{Uri.EscapeDataString(id)}", null, cancellationToken);

    public Task<ApiResult<OrderDetailDto>> UpdateOrderAsync(string id, UpdateOrderRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<OrderDetailDto>(HttpMethod.Put, $"api/orders/{Uri.EscapeDataString(id)}", request, cancellationToken);

    public Task<ApiResult<OrderDetailDto>> UpdateOrderStatusAsync(string id, UpdateOrderStatusRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<OrderDetailDto>(HttpMethod.Patch, $"api/orders/{Uri.EscapeDataString(id)}/status", request, cancellationToken);

    public Task<ApiResult<OrderPaymentsDto>> GetOrderPaymentsAsync(string orderId, CancellationToken cancellationToken = default) =>
        SendAsync<OrderPaymentsDto>(HttpMethod.Get, $"api/orders/{Uri.EscapeDataString(orderId)}/payments", null, cancellationToken);

    public Task<ApiResult<PaginatedResponse<CustomerListItemDto>>> ListCustomersAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var url = new StringBuilder("api/customers?page=").Append(Math.Max(1, page))
            .Append("&pageSize=").Append(Math.Clamp(pageSize, 1, PaginationDefaults.MaxPageSize));
        Append(url, "search", search);
        return SendAsync<PaginatedResponse<CustomerListItemDto>>(HttpMethod.Get, url.ToString(), null, cancellationToken);
    }

    public Task<ApiResult<PaginatedResponse<PaymentListItemDto>>> ListPaymentsAsync(int page, int pageSize, CancellationToken cancellationToken = default) =>
        SendAsync<PaginatedResponse<PaymentListItemDto>>(HttpMethod.Get,
            $"api/payments?page={Math.Max(1, page)}&pageSize={Math.Clamp(pageSize, 1, PaginationDefaults.MaxPageSize)}", null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<StaffNameDto>>> GetStaffNamesAsync(CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<StaffNameDto>>(HttpMethod.Get, "api/staff/names", null, cancellationToken);

    public Task<ApiResult<PaginatedResponse<AuditEventDto>>> ListActivityAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var url = new StringBuilder("api/audit?page=").Append(Math.Max(1, query.Page))
            .Append("&pageSize=").Append(Math.Clamp(query.PageSize, 1, PaginationDefaults.MaxPageSize));
        Append(url, "userId", query.UserId);
        Append(url, "action", query.Action);
        Append(url, "entityType", query.EntityType);
        Append(url, "entityId", query.EntityId);
        Append(url, "orderId", query.OrderId);
        Append(url, "customerId", query.CustomerId);
        Append(url, "source", query.Source);
        Append(url, "from", query.From?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Append(url, "to", query.To?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        Append(url, "search", query.Search);
        return SendAsync<PaginatedResponse<AuditEventDto>>(HttpMethod.Get, url.ToString(), null, cancellationToken);
    }

    public Task<ApiResult<StaffOverviewDto>> GetStaffAsync(CancellationToken cancellationToken = default) =>
        SendAsync<StaffOverviewDto>(HttpMethod.Get, "api/staff", null, cancellationToken);

    public Task<ApiResult<CreatedInvitationDto>> InviteAsync(CreateInvitationRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CreatedInvitationDto>(HttpMethod.Post, "api/staff/invitations", request, cancellationToken);

    public Task<ApiResult<bool>> RevokeInviteAsync(string invitationId, CancellationToken cancellationToken = default) =>
        SendAsync<bool>(HttpMethod.Delete, $"api/staff/invitations/{Uri.EscapeDataString(invitationId)}", null, cancellationToken);

    public Task<ApiResult<StaffMemberDto>> ChangeStaffRoleAsync(string userId, string role, CancellationToken cancellationToken = default) =>
        SendAsync<StaffMemberDto>(HttpMethod.Put, $"api/staff/{Uri.EscapeDataString(userId)}/role", new ChangeStaffRoleRequest { Role = role }, cancellationToken);

    public Task<ApiResult<StaffMemberDto>> SetStaffActiveAsync(string userId, bool active, CancellationToken cancellationToken = default) =>
        SendAsync<StaffMemberDto>(HttpMethod.Post, $"api/staff/{Uri.EscapeDataString(userId)}/{(active ? "reactivate" : "deactivate")}", null, cancellationToken);

    public Task<ApiResult<ReassignOrdersResultDto>> ReassignOrdersAsync(string userId, string? toUserId, CancellationToken cancellationToken = default) =>
        SendAsync<ReassignOrdersResultDto>(HttpMethod.Post, $"api/staff/{Uri.EscapeDataString(userId)}/reassign-orders",
            new ReassignOrdersRequest { ToUserId = string.IsNullOrWhiteSpace(toUserId) ? null : toUserId }, cancellationToken);

    public Task<ApiResult<AuthSessionPayload>> ChangePasswordAsync(NeedleBase.Application.Contracts.Auth.ChangePasswordRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<AuthSessionPayload>(HttpMethod.Post, "api/auth/change-password", request, cancellationToken);

    public Task<ApiResult<WhatsAppSettingsDto>> GetWhatsAppSettingsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<WhatsAppSettingsDto>(HttpMethod.Get, "api/whatsapp/settings", null, cancellationToken);

    public Task<ApiResult<WhatsAppSettingsDto>> UpdateWhatsAppSettingsAsync(UpdateWhatsAppSettingsRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<WhatsAppSettingsDto>(HttpMethod.Put, "api/whatsapp/settings", request, cancellationToken);

    public Task<ApiResult<WhatsAppConsentDto>> GetWhatsAppConsentAsync(string customerId, CancellationToken cancellationToken = default) =>
        SendAsync<WhatsAppConsentDto>(HttpMethod.Get, $"api/whatsapp/customers/{Uri.EscapeDataString(customerId)}/consent", null, cancellationToken);

    public Task<ApiResult<WhatsAppConsentDto>> SetWhatsAppConsentAsync(string customerId, bool optedIn, CancellationToken cancellationToken = default) =>
        SendAsync<WhatsAppConsentDto>(HttpMethod.Put, $"api/whatsapp/customers/{Uri.EscapeDataString(customerId)}/consent",
            new SetWhatsAppConsentRequest { OptedIn = optedIn }, cancellationToken);

    public Task<ApiResult<WhatsAppMessagePageDto>> ListWhatsAppMessagesAsync(string? orderId, int page, CancellationToken cancellationToken = default)
    {
        var url = new StringBuilder("api/whatsapp/messages?page=").Append(Math.Max(1, page));
        Append(url, "orderId", orderId);
        return SendAsync<WhatsAppMessagePageDto>(HttpMethod.Get, url.ToString(), null, cancellationToken);
    }

    public Task<ApiResult<bool>> RecordWhatsAppOpenedAsync(string orderId, string? status, CancellationToken cancellationToken = default) =>
        SendAsync<bool>(HttpMethod.Post, $"api/whatsapp/orders/{Uri.EscapeDataString(orderId)}/opened", new WhatsAppOpenedRequest { Status = status }, cancellationToken);

    internal static bool IsUnreachable(ApiProblem problem) =>
        problem.Status is 502 or 503 or 504 && problem.Code is null && problem.Detail is null;

    private static void Append(StringBuilder url, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            url.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value.Trim()));
        }
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        try
        {
            var client = _factory.CreateClient(ClientName);
            using var request = new HttpRequestMessage(method, url);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: Json);
            }

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var problem = await ApiProblem.ReadAsync(response, cancellationToken).ConfigureAwait(false);
                // TokenRefreshHandler turns "unreachable"/"timed out" into a bare 503/504, and a proxy in front of a
                // stopped Api answers 502: all mean "can't reach NeedleBase", not an Api error. A typed code wins.
                return IsUnreachable(problem) ? ApiResult<T>.Offline() : ApiResult<T>.Fail(problem);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent && typeof(T) == typeof(bool))
            {
                return ApiResult<T>.Ok((T)(object)true);
            }

            var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false);
            return value is null
                ? ApiResult<T>.Fail(new ApiProblem((int)response.StatusCode, null, null, null, new Dictionary<string, string[]>()))
                : ApiResult<T>.Ok(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser left (new click, Back, tab closed): normal, not an outage. Handled here, quietly, so it
            // doesn't surface as an unhandled TaskCanceledException; the page renders for nobody and is dropped.
            _logger.LogDebug("Api call {Method} {Url} cancelled: the browser closed the request", method, url);
            return ApiResult<T>.Offline();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Api call {Method} {Url} failed", method, url);
            return ApiResult<T>.Offline();
        }
    }
}
