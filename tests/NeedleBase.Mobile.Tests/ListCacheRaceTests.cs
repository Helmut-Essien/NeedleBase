using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

/// <summary>Re-audit fixes: version bumps after commit/migrate, filter change superseding an in-flight load.</summary>
public partial class SyncEdgeCaseTests
{
    [Fact]
    public async Task LocalDataVersion_BumpsAgainOnCommit_SoAReadBeforeCommitIsNotCachedAsCurrent()
    {
        await using var device = await DeviceAsync();
        await using var scope = device.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Customers.Where(c => c.Id == "none").ExecuteDeleteAsync();
        var seenByAListBeforeCommit = LocalDataVersion.Current;
        await tx.CommitAsync();
        Assert.True(LocalDataVersion.Current > seenByAListBeforeCommit);
    }

    [Fact]
    public async Task LocalDataVersion_BumpsOnMigrate_RawSqlBackfillInvalidatesLists()
    {
        await using var device = await DeviceAsync();
        await using var scope = device.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var before = LocalDataVersion.Current;
        await LocalSchemaMigrator.MigrateAsync(db);
        Assert.True(LocalDataVersion.Current > before);
    }

    [Fact]
    public async Task OrdersTab_FilterChangeDuringLoad_SupersedesIt_StaleRowsNeverLand()
    {
        await using var device = await DeviceAsync();
        var customer = (await device.GetRequiredService<ICustomerLocalService>()
            .SaveAsync(Biz, new CreateCustomerRequest("Esi", "024 555 0131", null, null))).Value!;
        var inner = device.GetRequiredService<IOrderLocalService>();
        await inner.SaveAsync(Biz, OneShirt(customer.Id));
        var gated = new FirstCallGatedOrders(inner);
        var vm = new OrdersViewModel(gated, new FakeAuth());

        var stale = vm.AppearAsync();            // first load blocks (old filter)
        await gated.FirstStarted.Task;
        await vm.ReloadForFilterAsync();         // not dropped by the IsBusy guard
        Assert.Single(vm.Items);

        gated.Release.SetResult();               // old load finishes late with an empty result
        await stale;
        Assert.Single(vm.Items);
        Assert.False(vm.IsBusy);
    }

    private sealed class FirstCallGatedOrders(IOrderLocalService inner) : IOrderLocalService
    {
        private int _calls;
        public readonly TaskCompletionSource FirstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<OrderListItemDto>> SearchAsync(string businessId, string? search, string? status, int take = 50, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstStarted.SetResult();
                await Release.Task;
                return [];
            }

            return await inner.SearchAsync(businessId, search, status, take, cancellationToken);
        }
        public Task<OrderDetailDto?> GetByIdAsync(string businessId, string id, CancellationToken cancellationToken = default) => inner.GetByIdAsync(businessId, id, cancellationToken);
        public Task<OrderCountsDto> GetCountsAsync(string businessId, CancellationToken cancellationToken = default) => inner.GetCountsAsync(businessId, cancellationToken);
        public Task<IReadOnlyList<OrderListItemDto>> ListAttentionAsync(string businessId, int take = 5, CancellationToken cancellationToken = default) => inner.ListAttentionAsync(businessId, take, cancellationToken);
        public Task<RecordResult<OrderDetailDto>> SaveAsync(string businessId, CreateOrderRequest request, CancellationToken cancellationToken = default) => inner.SaveAsync(businessId, request, cancellationToken);
        public Task<RecordResult<OrderDetailDto>> UpdateStatusAsync(string businessId, string id, string status, CancellationToken cancellationToken = default) => inner.UpdateStatusAsync(businessId, id, status, cancellationToken);
        public Task<RecordResult<OrderDetailDto>> UpdateAsync(string businessId, string id, NeedleBase.Application.Contracts.Orders.UpdateOrderRequest request, string? role, CancellationToken cancellationToken = default) => inner.UpdateAsync(businessId, id, request, role, cancellationToken);
    }
}
