using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Application.Licensing;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

/// <summary>The gate is process-wide, so these tests never run alongside others that save.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LicenseGateCollection
{
    public const string Name = "LicenseGate";
}

[Collection(LicenseGateCollection.Name)]
public sealed class LicenseGateMobileTests : IDisposable
{
    private const string Biz = "biz-license";

    private static readonly SubscriptionStatusDto ReadOnly = new()
    {
        State = SubscriptionStates.ReadOnly, IsReadOnly = true, Message = "Your NeedleBase license has expired. New changes are paused."
    };

    private static readonly SubscriptionStatusDto OwnerTask = new()
    {
        State = SubscriptionStates.NotAttached, OwnerOnly = true, Message = "Add your NeedleBase license key."
    };

    public LicenseGateMobileTests() => LocalSubscriptionGate.Clear();

    public void Dispose() => LocalSubscriptionGate.Clear();

    private static async Task<ServiceProvider> DeviceAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"needlebase-license-{Guid.NewGuid():N}.db3");
        var services = new ServiceCollection();
        services.AddDbContext<LocalDbContext>(o => o.UseSqlite($"Data Source={path};Pooling=False"));
        services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await LocalSchemaMigrator.MigrateAsync(scope.ServiceProvider.GetRequiredService<LocalDbContext>());
        return provider;
    }

    private static CreateCustomerRequest Customer(string name = "Efua Mensah") => new(name, "+233245550123", null, null);

    [Fact]
    public async Task ReadOnly_refuses_new_work_with_a_plain_message_and_queues_nothing()
    {
        await using var device = await DeviceAsync();
        var customers = device.GetRequiredService<ICustomerLocalService>();
        var (before, _) = await customers.SaveAsync(Biz, Customer());
        Assert.NotNull(before);

        LocalSubscriptionGate.Set(Biz, ReadOnly);
        var refused = await customers.SaveAsync(Biz, Customer("Kofi Boateng"));
        Assert.Equal(RecordError.SubscriptionReadOnly, refused.Error);
        Assert.Equal(LicenseErrors.ReadOnlyMessage, refused.Message);

        var update = await customers.UpdateAsync(Biz, before!.Id, new UpdateCustomerRequest("Efua M", "+233245550123", null, null));
        Assert.Equal(RecordError.SubscriptionReadOnly, update.Error);

        await using var scope = device.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        Assert.Equal(1, await db.Customers.CountAsync());
        Assert.Equal(1, await db.SyncQueue.CountAsync()); // only the change made before the pause, still waiting to sync

        // Viewing still works.
        Assert.NotNull(await customers.GetByIdAsync(Biz, before.Id));
    }

    [Fact]
    public async Task Sync_writes_are_never_blocked_and_unlock_restores_saves()
    {
        await using var device = await DeviceAsync();
        LocalSubscriptionGate.Set(Biz, ReadOnly);
        await using (var scope = device.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            db.SuppressSyncQueue = true; // pull-apply / push bookkeeping
            db.Customers.Add(NeedleBase.Domain.Entities.Customer.Create("01JLICENSEPULL0000000000001", Biz, "Ama Owusu", "+233245550124", null, null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        LocalSubscriptionGate.Set(Biz, SubscriptionStatusDto.Unlocked);
        var (saved, _) = await device.GetRequiredService<ICustomerLocalService>().SaveAsync(Biz, Customer());
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task Pull_sets_the_gate_and_an_old_server_without_status_changes_nothing()
    {
        await using var device = await DeviceAsync();
        var page = new SyncPullResponse([], [], [], [], "c1", false, DateTimeOffset.UtcNow) { Subscription = ReadOnly };
        await new SyncEngine(device.GetRequiredService<IServiceScopeFactory>(), new OnePull(page)).PullAsync(Biz);
        Assert.True(LocalSubscriptionGate.IsReadOnly);
        Assert.Equal(Biz, LocalSubscriptionGate.BusinessId);

        var old = new SyncPullResponse([], [], [], [], "c2", false, DateTimeOffset.UtcNow);
        await new SyncEngine(device.GetRequiredService<IServiceScopeFactory>(), new OnePull(old)).PullAsync(Biz);
        Assert.True(LocalSubscriptionGate.IsReadOnly);
    }

    [Fact]
    public async Task Wipe_clears_the_gate()
    {
        await using var device = await DeviceAsync();
        LocalSubscriptionGate.Set(Biz, ReadOnly);
        Assert.True(await new LocalDatabaseService(device.GetRequiredService<IServiceScopeFactory>(), new LocalDbOptions { DbPath = "unused" }).WipeAsync());
        Assert.Null(LocalSubscriptionGate.Current);
        Assert.False(LocalSubscriptionGate.IsReadOnly);
    }

    [Fact]
    public async Task Store_keeps_status_across_restarts_for_the_same_shop_only()
    {
        var storage = new MemoryStorage();
        _ = new SubscriptionStatusStore(storage);
        LocalSubscriptionGate.Set(Biz, ReadOnly);
        await WaitForAsync(() => storage.Values.ContainsKey(SubscriptionStatusStore.StorageKey));
        Assert.DoesNotContain("token", storage.Values[SubscriptionStatusStore.StorageKey], StringComparison.OrdinalIgnoreCase);

        LocalSubscriptionGate.Clear(); // "restart"
        await WaitForAsync(() => !storage.Values.ContainsKey(SubscriptionStatusStore.StorageKey));
        LocalSubscriptionGate.Set(Biz, ReadOnly);
        await WaitForAsync(() => storage.Values.ContainsKey(SubscriptionStatusStore.StorageKey));
        var saved = storage.Values[SubscriptionStatusStore.StorageKey];

        var restarted = new MemoryStorage();
        restarted.Values[SubscriptionStatusStore.StorageKey] = saved;
        LocalSubscriptionGate.Clear();
        var store = new SubscriptionStatusStore(restarted);
        await store.RestoreAsync("another-shop");
        Assert.Null(LocalSubscriptionGate.Current);
        await store.RestoreAsync(Biz);
        Assert.True(LocalSubscriptionGate.IsReadOnly);

        LocalSubscriptionGate.Clear();
        restarted.Values[SubscriptionStatusStore.StorageKey] = "{not json";
        await store.RestoreAsync(Biz);
        Assert.Null(LocalSubscriptionGate.Current);
    }

    [Fact]
    public async Task Store_a_slow_save_never_puts_back_a_cleared_status()
    {
        LocalSubscriptionGate.Clear();
        var storage = new SlowStorage();
        _ = new SubscriptionStatusStore(storage);
        LocalSubscriptionGate.Set(Biz, ReadOnly);
        LocalSubscriptionGate.Clear(); // sign-out wipe right after a pull
        await Task.Delay(400);
        Assert.False(storage.Values.ContainsKey(SubscriptionStatusStore.StorageKey));
    }

    private sealed class SlowStorage : ISecureStorageService
    {
        public System.Collections.Concurrent.ConcurrentDictionary<string, string> Values { get; } = new();
        public async Task SetAsync(string key, string value) { await Task.Delay(150); Values[key] = value; }
        public Task<string?> GetAsync(string key) => Task.FromResult(Values.TryGetValue(key, out var v) ? v : null);
        public void Remove(string key) => Values.TryRemove(key, out _);
    }

    [Theory]
    [InlineData(BusinessRole.Owner, true)]
    [InlineData(BusinessRole.Manager, false)]
    [InlineData(BusinessRole.Tailor, false)]
    public void Banner_owner_tasks_show_only_to_the_owner_problems_to_everyone(string role, bool ownerSees)
    {
        Assert.Equal(ownerSees, HomeViewModel.LicenseBannerText(OwnerTask, role) is not null);
        Assert.Equal(ReadOnly.Message, HomeViewModel.LicenseBannerText(ReadOnly, role));
        Assert.Null(HomeViewModel.LicenseBannerText(SubscriptionStatusDto.Unlocked, role));
        Assert.Null(HomeViewModel.LicenseBannerText(null, role));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class MemoryStorage : ISecureStorageService
    {
        public System.Collections.Concurrent.ConcurrentDictionary<string, string> Values { get; } = new();
        public Task SetAsync(string key, string value) { Values[key] = value; return Task.CompletedTask; }
        public Task<string?> GetAsync(string key) => Task.FromResult(Values.TryGetValue(key, out var v) ? v : null);
        public void Remove(string key) => Values.TryRemove(key, out _);
    }

    private sealed class OnePull(SyncPullResponse page) : ISyncApi
    {
        public Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken) => Task.FromResult(new SyncPushResponse([]));
        public Task<SyncPullResponse> PullAsync(SyncPullRequest request, CancellationToken cancellationToken) => Task.FromResult(page);
    }
}
