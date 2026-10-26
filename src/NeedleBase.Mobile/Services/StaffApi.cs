using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Staff;
using NeedleBase.Application.Contracts.WhatsApp;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Configuration;

namespace NeedleBase.Mobile.Services;

/// <summary>Result of an online staff call: value, or friendly copy (never throws for network/HTTP failures).</summary>
public sealed record StaffCallResult<T>(T? Value, string? Error, bool IsOffline = false)
{
    public bool IsSuccess => Error is null;
    public static StaffCallResult<T> Ok(T? value) => new(value, null);
}

/// <summary>Staff management is online-only (not synced): list, invite, revoke, role, access.</summary>
public interface IStaffApi
{
    Task<StaffCallResult<StaffOverviewDto>> GetAsync(CancellationToken cancellationToken = default);
    Task<StaffCallResult<CreatedInvitationDto>> InviteAsync(CreateInvitationRequest request, CancellationToken cancellationToken = default);
    Task<StaffCallResult<bool>> RevokeAsync(string invitationId, CancellationToken cancellationToken = default);
    Task<StaffCallResult<StaffMemberDto>> ChangeRoleAsync(string userId, string role, CancellationToken cancellationToken = default);
    Task<StaffCallResult<StaffMemberDto>> SetActiveAsync(string userId, bool active, CancellationToken cancellationToken = default);
    Task<StaffCallResult<ReassignOrdersResultDto>> ReassignOrdersAsync(string fromUserId, string? toUserId, CancellationToken cancellationToken = default);
}

/// <summary>Best-effort tap-to-send audit ("opened", not "sent"); offline it simply isn't recorded.</summary>
public interface IWhatsAppOpenedApi
{
    Task RecordOpenedAsync(string orderId, string? status, CancellationToken cancellationToken = default);
}

/// <summary>WhatsApp settings and message log (Owner/Manager, online only).</summary>
public interface IWhatsAppAdminApi
{
    Task<StaffCallResult<WhatsAppSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<StaffCallResult<WhatsAppSettingsDto>> SaveSettingsAsync(bool enabled, IReadOnlyList<string> statuses, CancellationToken cancellationToken = default);
    Task<StaffCallResult<WhatsAppMessagePageDto>> GetMessagesAsync(int page, CancellationToken cancellationToken = default);
}

public sealed class StaffApi : IStaffApi, IWhatsAppOpenedApi, IWhatsAppAdminApi, IWhatsAppConsentApi
{
    public Task<StaffCallResult<WhatsAppSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<WhatsAppSettingsDto>(HttpMethod.Get, "api/whatsapp/settings", null, cancellationToken);

    public Task<StaffCallResult<WhatsAppSettingsDto>> SaveSettingsAsync(bool enabled, IReadOnlyList<string> statuses, CancellationToken cancellationToken = default) =>
        SendAsync<WhatsAppSettingsDto>(HttpMethod.Put, "api/whatsapp/settings",
            new UpdateWhatsAppSettingsRequest { Enabled = enabled, Statuses = statuses.ToList() }, cancellationToken);

    public Task<StaffCallResult<WhatsAppMessagePageDto>> GetMessagesAsync(int page, CancellationToken cancellationToken = default) =>
        SendAsync<WhatsAppMessagePageDto>(HttpMethod.Get, $"api/whatsapp/messages?page={Math.Max(1, page)}", null, cancellationToken);

    public Task<ConsentPushResult> GetConsentAsync(string customerId, CancellationToken cancellationToken = default) =>
        ConsentCallAsync(HttpMethod.Get, customerId, null, cancellationToken);

    public Task<ConsentPushResult> SetConsentAsync(string customerId, bool optedIn, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default) =>
        ConsentCallAsync(HttpMethod.Put, customerId, new SetWhatsAppConsentRequest { OptedIn = optedIn, ChangedAtUtc = changedAtUtc, DeviceNowUtc = DateTimeOffset.UtcNow }, cancellationToken);

    private async Task<ConsentPushResult> ConsentCallAsync(HttpMethod method, string customerId, object? body, CancellationToken cancellationToken)
    {
        var client = _factory.CreateClient(HttpSyncApi.ClientName);
        using var request = new HttpRequestMessage(method, new Uri(new Uri(_settings.BaseUrl), $"api/whatsapp/customers/{Uri.EscapeDataString(customerId)}/consent"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: NeedleBase.Mobile.Data.Sync.SyncJson.Options);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.StatusCode switch
            {
                _ when response.IsSuccessStatusCode => new ConsentPushResult(ConsentPushOutcome.Saved,
                    await response.Content.ReadFromJsonAsync<WhatsAppConsentDto>(NeedleBase.Mobile.Data.Sync.SyncJson.Options, cancellationToken).ConfigureAwait(false)),
                HttpStatusCode.NotFound => new ConsentPushResult(ConsentPushOutcome.NotFound),
                HttpStatusCode.Forbidden => new ConsentPushResult(ConsentPushOutcome.Forbidden),
                _ => new ConsentPushResult(ConsentPushOutcome.Failed)
            };
        }
        catch (HttpRequestException)
        {
            return new ConsentPushResult(ConsentPushOutcome.Offline);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ConsentPushResult(ConsentPushOutcome.Offline);
        }
        catch (System.Text.Json.JsonException)
        {
            return new ConsentPushResult(ConsentPushOutcome.Failed);
        }
    }

    public async Task RecordOpenedAsync(string orderId, string? status, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync<bool>(HttpMethod.Post, $"api/whatsapp/orders/{Uri.EscapeDataString(orderId)}/opened",
                new NeedleBase.Application.Contracts.WhatsApp.WhatsAppOpenedRequest { Status = status }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // never surfaces: the customer message already opened
        }
    }

    public const string OfflineMessage = "You're offline. Staff changes need a connection.";
    private readonly IHttpClientFactory _factory;
    private readonly ApiSettings _settings;

    public StaffApi(IHttpClientFactory factory, IOptions<ApiSettings> settings)
    {
        _factory = factory;
        _settings = settings.Value;
    }

    public Task<StaffCallResult<StaffOverviewDto>> GetAsync(CancellationToken cancellationToken = default) =>
        SendAsync<StaffOverviewDto>(HttpMethod.Get, "api/staff", null, cancellationToken);

    public Task<StaffCallResult<CreatedInvitationDto>> InviteAsync(CreateInvitationRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<CreatedInvitationDto>(HttpMethod.Post, "api/staff/invitations", request, cancellationToken);

    public Task<StaffCallResult<bool>> RevokeAsync(string invitationId, CancellationToken cancellationToken = default) =>
        SendAsync<bool>(HttpMethod.Delete, $"api/staff/invitations/{Uri.EscapeDataString(invitationId)}", null, cancellationToken);

    public Task<StaffCallResult<StaffMemberDto>> ChangeRoleAsync(string userId, string role, CancellationToken cancellationToken = default) =>
        SendAsync<StaffMemberDto>(HttpMethod.Put, $"api/staff/{Uri.EscapeDataString(userId)}/role", new ChangeStaffRoleRequest { Role = role }, cancellationToken);

    public Task<StaffCallResult<StaffMemberDto>> SetActiveAsync(string userId, bool active, CancellationToken cancellationToken = default) =>
        SendAsync<StaffMemberDto>(HttpMethod.Post, $"api/staff/{Uri.EscapeDataString(userId)}/{(active ? "reactivate" : "deactivate")}", null, cancellationToken);

    public Task<StaffCallResult<ReassignOrdersResultDto>> ReassignOrdersAsync(string fromUserId, string? toUserId, CancellationToken cancellationToken = default) =>
        SendAsync<ReassignOrdersResultDto>(HttpMethod.Post, $"api/staff/{Uri.EscapeDataString(fromUserId)}/reassign-orders", new ReassignOrdersRequest { ToUserId = toUserId }, cancellationToken);

    private async Task<StaffCallResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var client = _factory.CreateClient(HttpSyncApi.ClientName);
        using var request = new HttpRequestMessage(method, new Uri(new Uri(_settings.BaseUrl), path));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: NeedleBase.Mobile.Data.Sync.SyncJson.Options);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(bool))
                {
                    return StaffCallResult<T>.Ok((T)(object)true);
                }

                return StaffCallResult<T>.Ok(await response.Content.ReadFromJsonAsync<T>(NeedleBase.Mobile.Data.Sync.SyncJson.Options, cancellationToken).ConfigureAwait(false));
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new StaffCallResult<T>(default, "Your session has ended. Sign in again.");
            }

            return new StaffCallResult<T>(default, StaffErrorMessages.ForProblem(await ApiProblem.ReadAsync(response, cancellationToken).ConfigureAwait(false)));
        }
        catch (HttpRequestException)
        {
            return new StaffCallResult<T>(default, OfflineMessage, IsOffline: true);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new StaffCallResult<T>(default, "NeedleBase took too long to answer. Try again.", IsOffline: true);
        }
    }
}
