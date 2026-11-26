using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Audit;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Domain.Enums;
using NeedleBase.Infrastructure.Persistence;

namespace NeedleBase.Api.Tests;

/// <summary>Code-gaps G14: PATCH api/orders/{id}/assignee, and the same field through sync.</summary>
public sealed partial class StaffApiTests
{
    [Theory]
    [InlineData(BusinessRole.Owner)]
    [InlineData(BusinessRole.Manager)]
    public async Task Assign_and_unassign_by_owner_or_manager_writes_activity(string role)
    {
        var owner = await RegisterAsync("as" + role[..2]);
        var (tailorId, _) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        var token = owner.Token;
        if (role == BusinessRole.Manager)
        {
            token = (await AddMemberAsync(owner.Token, BusinessRole.Manager)).Tokens.AccessToken!;
        }

        var orderId = await CreateOrderAsync(owner.Token, null);
        var assigned = await AssignAsync(token, orderId, tailorId);
        Assert.Equal(HttpStatusCode.OK, assigned.Status);
        Assert.Equal(tailorId, assigned.Order!.AssignedToUserId);

        var cleared = await AssignAsync(token, orderId, null, assigned.Order.DateModifiedUtc);
        Assert.Equal(HttpStatusCode.OK, cleared.Status);
        Assert.Null(cleared.Order!.AssignedToUserId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.AuditEvents.IgnoreQueryFilters()
            .AnyAsync(a => a.BusinessId == owner.BusinessId && a.Action == AuditActions.OrderAssigned));
    }

    [Fact]
    public async Task Assign_rejects_tailor_inactive_cross_shop_and_stale()
    {
        var owner = await RegisterAsync("asrej");
        var (tailorId, tailor) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        var (leaverId, _) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        var other = await RegisterAsync("asoth");
        var (outsiderId, _) = await AddMemberAsync(other.Token, BusinessRole.Tailor);
        var orderId = await CreateOrderAsync(owner.Token, null);

        // Tailor: 403, even to assign themselves.
        var byTailor = await AssignAsync(tailor.AccessToken!, orderId, tailorId);
        Assert.Equal(HttpStatusCode.Forbidden, byTailor.Status);
        Assert.Equal(OrderErrorMessages.EditForbidden, byTailor.Code);

        // Another shop's member, and a deactivated member: 400 ASSIGNEE_INVALID.
        var cross = await AssignAsync(owner.Token, orderId, outsiderId);
        Assert.Equal(HttpStatusCode.BadRequest, cross.Status);
        Assert.Equal(OrderErrorMessages.AssigneeInvalid, cross.Code);
        await AssertStatus(HttpStatusCode.OK, HttpMethod.Post, $"/api/staff/{leaverId}/deactivate", owner.Token);
        var inactive = await AssignAsync(owner.Token, orderId, leaverId);
        Assert.Equal(OrderErrorMessages.AssigneeInvalid, inactive.Code);

        // Someone else's order (other shop) is not found; an old base is 409.
        var foreign = await AssignAsync(other.Token, orderId, outsiderId);
        Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
        var stale = await AssignAsync(owner.Token, orderId, tailorId, DateTimeOffset.UtcNow.AddDays(-1));
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Equal(StaleUpdate.Code, stale.Code);

        var order = await OrderAsync(owner.Token, orderId);
        Assert.Null(order.AssignedToUserId); // nothing above changed it
    }

    [Fact]
    public async Task Assign_keeps_header_and_sends_assignment_email()
    {
        var mail = UseEmail();
        var owner = await RegisterAsync("asmail");
        var (tailorId, tailor) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        var orderId = await CreateOrderAsync(owner.Token, null);
        var before = await OrderAsync(owner.Token, orderId);

        var assigned = await AssignAsync(owner.Token, orderId, tailorId);
        Assert.Equal(HttpStatusCode.OK, assigned.Status);
        Assert.Equal(before.DueDate, assigned.Order!.DueDate);
        Assert.Equal(before.Notes, assigned.Order.Notes);
        Assert.Equal(before.Lines.Count, assigned.Order.Lines.Count);

        var message = await mail.WaitForAsync(tailor.User!.Email, "order_assigned");
        Assert.Contains("Efua Mensah", message.TextBody);

        // The tailor now sees it as theirs and may move it on.
        Assert.Equal(HttpStatusCode.OK, await SetStatusAsync(tailor.AccessToken!, orderId, OrderStatus.Cutting));
    }

    [Fact]
    public async Task Sync_push_carries_assign_and_unassign_and_pull_returns_it()
    {
        var owner = await RegisterAsync("assync");
        var (tailorId, tailor) = await AddMemberAsync(owner.Token, BusinessRole.Tailor);
        var orderId = await CreateOrderAsync(owner.Token, null);

        var pulled = await PullSyncOrderAsync(owner.Token, orderId);
        var assigned = await PushOrderAsync(owner.Token, pulled with { AssignedToUserId = tailorId }, pulled.DateModifiedUtc);
        Assert.Equal(SyncOpStatus.Applied, assigned.Status);
        Assert.Equal(tailorId, (await PullSyncOrderAsync(tailor.AccessToken!, orderId)).AssignedToUserId);

        // A Tailor's device can't change the assignee.
        var tailorView = await PullSyncOrderAsync(tailor.AccessToken!, orderId);
        var blocked = await PushOrderAsync(tailor.AccessToken!, tailorView with { AssignedToUserId = null }, tailorView.DateModifiedUtc);
        Assert.NotEqual(SyncOpStatus.Applied, blocked.Status);

        var latest = await PullSyncOrderAsync(owner.Token, orderId);
        var cleared = await PushOrderAsync(owner.Token, latest with { AssignedToUserId = null }, latest.DateModifiedUtc);
        Assert.Equal(SyncOpStatus.Applied, cleared.Status);
        Assert.Null((await OrderAsync(owner.Token, orderId)).AssignedToUserId);
    }

    private async Task<(HttpStatusCode Status, OrderDetailDto? Order, string? Code)> AssignAsync(
        string token, string orderId, string? assignee, DateTimeOffset? baseModified = null)
    {
        using var response = await SendAsync(HttpMethod.Patch, $"/api/orders/{orderId}/assignee", token,
            new UpdateOrderAssigneeRequest { AssignedToUserId = assignee, BaseModifiedUtc = baseModified });
        return response.IsSuccessStatusCode
            ? (response.StatusCode, await response.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions), null)
            : (response.StatusCode, null, await CodeAsync(response));
    }

    private async Task<SyncOrderDto> PullSyncOrderAsync(string token, string orderId)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/sync/pull", token, new SyncPullRequest { Limit = 200 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<SyncPullResponse>(JsonOptions))!;
        return Assert.Single(page.Orders, o => o.Id == orderId);
    }

    private async Task<SyncPushResult> PushOrderAsync(string token, SyncOrderDto order, DateTimeOffset baseModified)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/sync/push", token, new SyncPushRequest
        {
            Operations =
            [
                new SyncPushOperation
                {
                    OpId = IdGenerator.NewId(), EntityType = SyncEntityType.Order, EntityId = order.Id,
                    BaseModifiedUtc = baseModified, Order = order
                }
            ]
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.Single((await response.Content.ReadFromJsonAsync<SyncPushResponse>(JsonOptions))!.Results);
    }
}
