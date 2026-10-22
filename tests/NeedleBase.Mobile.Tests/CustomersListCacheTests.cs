using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Abstractions;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

/// <summary>Customers tab reuses its rows until a local write, a search change or a session change.</summary>
public partial class SyncEdgeCaseTests
{
    private sealed class CountingCustomers(ICustomerLocalService inner) : ICustomerLocalService
    {
        public int Searches;
        public Task<IReadOnlyList<CustomerListItemDto>> SearchAsync(string businessId, string? search, int take = 50, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Searches);
            return inner.SearchAsync(businessId, search, take, cancellationToken);
        }
        public Task<IReadOnlyList<CustomerListItemDto>> RecentAsync(string businessId, int take = 8, CancellationToken cancellationToken = default) => inner.RecentAsync(businessId, take, cancellationToken);
        public Task<IReadOnlyList<CustomerListItemDto>> FindByPhoneAsync(string businessId, string? phone, string? excludeId, CancellationToken cancellationToken = default) => inner.FindByPhoneAsync(businessId, phone, excludeId, cancellationToken);
        public Task<CustomerDetailDto?> GetByIdAsync(string businessId, string id, CancellationToken cancellationToken = default) => inner.GetByIdAsync(businessId, id, cancellationToken);
        public Task<RecordResult<CustomerDetailDto>> SaveAsync(string businessId, CreateCustomerRequest request, CancellationToken cancellationToken = default) => inner.SaveAsync(businessId, request, cancellationToken);
        public Task<RecordResult<CustomerDetailDto>> UpdateAsync(string businessId, string id, UpdateCustomerRequest request, CancellationToken cancellationToken = default) => inner.UpdateAsync(businessId, id, request, cancellationToken);
        public Task<CustomerDeleteResult> DeleteAsync(string businessId, string id, CancellationToken cancellationToken = default) => inner.DeleteAsync(businessId, id, cancellationToken);
    }

    [Fact]
    public async Task CustomersTab_ReusesRows_UntilSaveEditDeleteSyncWipeOrSessionClear()
    {
        await using var device = await DeviceAsync();
        var inner = device.GetRequiredService<ICustomerLocalService>();
        var ama = (await inner.SaveAsync(Biz, new CreateCustomerRequest("Ama", "024 555 0200", null, null))).Value!;
        var customers = new CountingCustomers(inner);
        var vm = new CustomersViewModel(customers, new FakeAuth());

        await vm.AppearAsync();
        Assert.Equal(1, customers.Searches);
        await vm.AppearAsync();
        Assert.Equal(1, customers.Searches);           // cache hit

        await inner.SaveAsync(Biz, new CreateCustomerRequest("Kojo", "024 555 0201", null, null));
        await vm.AppearAsync();
        Assert.Equal(2, customers.Searches);
        Assert.Equal(2, vm.Items.Count);

        await inner.UpdateAsync(Biz, ama.Id, new UpdateCustomerRequest("Ama Owusu", ama.Phone, null, null));
        await vm.AppearAsync();
        Assert.Equal(3, customers.Searches);
        Assert.Contains(vm.Items, c => c.Name == "Ama Owusu");

        await using (var scope = device.CreateAsyncScope())  // sync pull applying a tombstone
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            await db.Customers.Where(c => c.Id == ama.Id).ExecuteDeleteAsync();
        }

        await vm.AppearAsync();
        Assert.Equal(4, customers.Searches);
        Assert.DoesNotContain(vm.Items, c => c.Id == ama.Id);

        var service = new LocalDatabaseService(device.GetRequiredService<IServiceScopeFactory>(), new LocalDbOptions { DbPath = "unused" });
        Assert.True(await service.WipeAsync());       // deactivation / sign-out wipe
        await vm.AppearAsync();
        Assert.Equal(5, customers.Searches);
        Assert.Empty(vm.Items);

        vm.ClearSessionState();                       // account switch
        await vm.AppearAsync();
        Assert.Equal(6, customers.Searches);
    }

    [Fact]
    public async Task CustomersTab_SpinnerOnlyWhenEmpty()
    {
        await using var device = await DeviceAsync();
        var vm = new CustomersViewModel(device.GetRequiredService<ICustomerLocalService>(), new FakeAuth());
        vm.IsBusy = true;
        Assert.True(vm.ShowBusyIndicator);
        vm.Items.Add(new CustomerListItemDto("c1", "Esi", "+233245550300", DateTimeOffset.UtcNow));
        Assert.False(vm.ShowBusyIndicator);
    }
}
