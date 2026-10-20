using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Sync;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Configuration;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.Services;

namespace NeedleBase.Mobile.Tests;

/// <summary>Slice 4 mobile sync: queue, push ack, pull apply, tombstones, conflicts, transient failures.</summary>
public class SyncEngineTests
{
    private const string Biz = "biz-sync";

    [Fact]
    public async Task LocalSave_EnqueuesOnce_AndCoalescesEdits()
    {
        await using var device = await DeviceAsync();
        var id = await AddCustomerAsync(device, "Efua Mensah");
        var customers = device.GetRequiredService<ICustomerLocalService>();
        await customers.UpdateAsync(Biz, id, new UpdateCustomerRequest("Efua M.", "0245550123", null, null));

        var queue = await QueueAsync(device);
        var item = Assert.Single(queue);
        Assert.Equal(SyncEntityType.Customer, item.EntityType);
        Assert.Null(item.BaseModifiedUtc); // still a create
        Assert.Contains("Efua M.", item.PayloadJson);
    }

    [Fact]
    public async Task CreateThenDelete_BeforeSync_CancelsOut()
    {
        await using var device = await DeviceAsync();
        var id = await AddCustomerAsync(device, "Kofi");
        await device.GetRequiredService<ICustomerLocalService>().DeleteAsync(Biz, id);
        Assert.Empty(await QueueAsync(device));
    }

    [Fact]
    public async Task Push_ThenFreshDevicePull_RestoresData_WithoutEnqueueOrDuplicates()
    {
        var server = new FakeSyncServer();
        await using var phone = await DeviceAsync();
        var id = await AddCustomerAsync(phone, "Ama Owusu");

        await Engine(phone, server).RunAsync(Biz);
        await Engine(phone, server).RunAsync(Biz); // re-run is a no-op (idempotent)

        Assert.Empty(await QueueAsync(phone));
        Assert.Single(server.Customers);

        // Reinstall / second device: empty database, same account.
        await using var tablet = await DeviceAsync();
        await Engine(tablet, server).RunAsync(Biz);
        var list = await tablet.GetRequiredService<ICustomerLocalService>().SearchAsync(Biz, string.Empty);
        Assert.Equal(id, Assert.Single(list).Id);
        Assert.Empty(await QueueAsync(tablet)); // pulled rows are not re-queued
    }

    [Fact]
    public async Task TransientFailure_KeepsQueue()
    {
        var server = new FakeSyncServer { FailWith = new SyncTransientException("offline") };
        await using var device = await DeviceAsync();
        await AddCustomerAsync(device, "Yaw");

        await Assert.ThrowsAsync<SyncTransientException>(() => Engine(device, server).RunAsync(Biz));

        var item = Assert.Single(await QueueAsync(device));
        Assert.Equal(SyncQueueState.Pending, item.State);
    }

    [Fact]
    public async Task Tombstone_RemovesLocalRow()
    {
        var server = new FakeSyncServer();
        await using var device = await DeviceAsync();
        var id = await AddCustomerAsync(device, "Esi");
        await Engine(device, server).RunAsync(Biz);

        server.Customers.Remove(id);
        server.Deleted.Add(new SyncDeletedItemDto(SyncEntityType.Customer, id, DateTimeOffset.UtcNow));
        await Engine(device, server).RunAsync(Biz);

        Assert.Empty(await device.GetRequiredService<ICustomerLocalService>().SearchAsync(Biz, string.Empty));
        Assert.Empty(await QueueAsync(device));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Conflict_IsShown_ThenResolved(bool keepMine)
    {
        var server = new FakeSyncServer();
        await using var device = await DeviceAsync();
        var id = await AddCustomerAsync(device, "Abena");
        await Engine(device, server).RunAsync(Biz);

        // Another device edits on the server; this device edits offline.
        var theirs = server.Customers[id] with { Name = "Abena (theirs)", DateModifiedUtc = DateTimeOffset.UtcNow.AddSeconds(1) };
        server.Customers[id] = theirs;
        await device.GetRequiredService<ICustomerLocalService>()
            .UpdateAsync(Biz, id, new UpdateCustomerRequest("Abena (mine)", "0245550123", null, null));

        var engine = Engine(device, server);
        await engine.RunAsync(Biz);
        var conflict = Assert.Single(await QueueAsync(device));
        Assert.Equal(SyncQueueState.Conflict, conflict.State);
        Assert.Equal(1, (await engine.GetSummaryAsync(Biz)).Conflicts);
        // The local edit is kept while unresolved (pull does not overwrite it).
        Assert.Equal("Abena (mine)", (await device.GetRequiredService<ICustomerLocalService>().GetByIdAsync(Biz, id))!.Name);

        await engine.ResolveConflictAsync(Biz, conflict.Id, keepMine);
        await engine.RunAsync(Biz);

        Assert.Empty(await QueueAsync(device));
        var expected = keepMine ? "Abena (mine)" : "Abena (theirs)";
        Assert.Equal(expected, server.Customers[id].Name);
        Assert.Equal(expected, (await device.GetRequiredService<ICustomerLocalService>().GetByIdAsync(Biz, id))!.Name);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{}", typeof(SyncTransientException))]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", typeof(SyncTransientException))]
    [InlineData(HttpStatusCode.BadRequest, "{\"code\":\"SYNC_CURSOR_INVALID\"}", typeof(SyncCursorInvalidException))]
    [InlineData(HttpStatusCode.Unauthorized, "{}", typeof(SyncSessionExpiredException))]
    public async Task HttpSyncApi_MapsFailures(HttpStatusCode status, string body, Type expected)
    {
        var api = new HttpSyncApi(new StubFactory(new StubHandler(status, body)), Options.Create(new ApiSettings()));
        var ex = await Record.ExceptionAsync(() => api.PullAsync(new SyncPullRequest(), CancellationToken.None));
        Assert.IsType(expected, ex);
    }

    [Fact]
    public async Task HttpSyncApi_NetworkDown_IsTransient()
    {
        var api = new HttpSyncApi(new StubFactory(new ThrowingHandler()), Options.Create(new ApiSettings()));
        await Assert.ThrowsAsync<SyncTransientException>(() => api.PushAsync(new SyncPushRequest(), CancellationToken.None));
    }

    // ---------------------------------------------------------------- helpers

    private static SyncEngine Engine(ServiceProvider device, ISyncApi api) =>
        new(device.GetRequiredService<IServiceScopeFactory>(), api);

    private static async Task<string> AddCustomerAsync(ServiceProvider device, string name)
    {
        var (customer, error) = await device.GetRequiredService<ICustomerLocalService>()
            .SaveAsync(Biz, new CreateCustomerRequest(name, "0245550123", null, null));
        Assert.Null(error);
        return customer!.Id;
    }

    private static async Task<List<SyncQueueItem>> QueueAsync(ServiceProvider device)
    {
        await using var scope = device.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LocalDbContext>().SyncQueue.AsNoTracking().ToListAsync();
    }

    private static async Task<ServiceProvider> DeviceAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"needlebase-sync-{Guid.NewGuid():N}.db3");
        var services = new ServiceCollection();
        services.AddDbContext<LocalDbContext>(o => o.UseSqlite($"Data Source={path};Pooling=False"));
        services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await LocalSchemaMigrator.MigrateAsync(scope.ServiceProvider.GetRequiredService<LocalDbContext>());
        return provider;
    }

    /// <summary>In-memory server with the API's customer rules: idempotent create, stale base = conflict, tombstones.</summary>
    private sealed class FakeSyncServer : ISyncApi
    {
        public Dictionary<string, SyncCustomerDto> Customers { get; } = new();
        public List<SyncDeletedItemDto> Deleted { get; } = new();
        public Exception? FailWith { get; set; }

        public Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken)
        {
            if (FailWith is not null) throw FailWith;
            var results = new List<SyncPushResult>();
            foreach (var op in request.Operations)
            {
                Customers.TryGetValue(op.EntityId, out var existing);
                if (op.Operation == SyncOperation.Delete)
                {
                    Customers.Remove(op.EntityId);
                    results.Add(new SyncPushResult(op.OpId, SyncOpStatus.Applied, null, null, null, null, null));
                    continue;
                }

                var incoming = op.Customer!;
                if (existing is not null && op.BaseModifiedUtc is { } b && b != existing.DateModifiedUtc)
                {
                    results.Add(new SyncPushResult(op.OpId, SyncOpStatus.Conflict, SyncErrorCodes.StaleBase, null, existing, null, null));
                    continue;
                }

                var saved = incoming with { DateModifiedUtc = DateTimeOffset.UtcNow };
                Customers[op.EntityId] = saved;
                results.Add(new SyncPushResult(op.OpId, SyncOpStatus.Applied, null, null, saved, null, null));
            }

            return Task.FromResult(new SyncPushResponse(results));
        }

        public Task<SyncPullResponse> PullAsync(SyncPullRequest request, CancellationToken cancellationToken)
        {
            if (FailWith is not null) throw FailWith;
            return Task.FromResult(new SyncPullResponse(
                Customers.Values.ToList(), [], [], Deleted.ToList(), "c", false, DateTimeOffset.UtcNow));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Network unreachable");
    }
}
