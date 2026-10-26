using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;

namespace NeedleBase.Mobile.Tests;

/// <summary>Per-row pending-sync badge (Slice 8): queued → "Not synced yet", failed/conflict → "Needs attention", synced → none.</summary>
public partial class SyncEdgeCaseTests
{
    [Fact]
    public async Task CustomerRows_ShowBadge_WhileQueued_AttentionOnFailure_ClearedWhenAcked()
    {
        await using var device = await DeviceAsync();
        var customers = device.GetRequiredService<ICustomerLocalService>();
        var ama = (await customers.SaveAsync(Biz, new CreateCustomerRequest("Ama", "024 555 0300", null, null))).Value!;

        var row = Assert.Single(await customers.SearchAsync(Biz, null));
        Assert.Equal("Not synced yet", row.SyncBadge);
        Assert.False(row.SyncNeedsAttention);
        Assert.Equal("Not synced yet", Assert.Single(await customers.RecentAsync(Biz)).SyncBadge);

        await using (var scope = device.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            await db.SyncQueue.Where(q => q.EntityId == ama.Id).ExecuteUpdateAsync(s => s.SetProperty(q => q.State, SyncQueueState.Failed));
        }

        row = Assert.Single(await customers.SearchAsync(Biz, null));
        Assert.Equal("Needs attention", row.SyncBadge);
        Assert.True(row.SyncNeedsAttention);

        await using (var scope = device.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            await db.SyncQueue.Where(q => q.EntityId == ama.Id).ExecuteDeleteAsync(); // acked
        }

        row = Assert.Single(await customers.SearchAsync(Biz, null));
        Assert.False(row.HasSyncBadge);
        Assert.Null(row.SyncBadge);
    }

    [Theory]
    [InlineData(null, "pending", "pending")]
    [InlineData("pending", "conflict", "attention")]
    [InlineData("attention", "pending", "attention")]
    [InlineData(null, "failed", "attention")]
    public void Badge_takes_the_worst_state(string? current, string queueState, string expected) =>
        Assert.Equal(expected, LocalSyncBadge.Combine(current, queueState));

    [Fact]
    public void Server_list_json_never_carries_the_device_badge()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new CustomerListItemDto("c1", "Ama", "+233245550123", DateTimeOffset.UnixEpoch));
        Assert.DoesNotContain("localSyncState", json, StringComparison.OrdinalIgnoreCase);
    }
}
