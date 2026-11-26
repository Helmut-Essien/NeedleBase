using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

public class HomeViewModelTests
{
    [Fact]
    public void HomeViewModel_HasScaffoldTitle()
    {
        var vm = new HomeViewModel(
            new StubAuthService(),
            new StubCustomerLocalService(),
            new StubOrderLocalService());
        Assert.Equal("Home", vm.Title);
        Assert.False(string.IsNullOrWhiteSpace(vm.Tagline));
    }

    // G13: Tailors have no Payments tab, so Home must not offer or navigate to it.
    [Theory]
    [InlineData(BusinessRole.Owner, true)]
    [InlineData(BusinessRole.Manager, true)]
    [InlineData(BusinessRole.Tailor, false)]
    [InlineData(null, true)]
    public void CanRecordPayments_MatchesPaymentsTabRule(string? role, bool expected)
    {
        Assert.Equal(expected, HomeViewModel.CanRecordPayments(role));
        Assert.Equal(
            role is null || NeedleBase.Application.Payments.PaymentRules.CanManage(role),
            HomeViewModel.CanRecordPayments(role));
    }

    [Fact]
    public void CanManagePayments_StartsHidden_UntilLoad()
    {
        var vm = NewVm(BusinessRole.Owner);
        Assert.False(vm.CanManagePayments);
    }

    [Theory]
    [InlineData(BusinessRole.Owner, true)]
    [InlineData(BusinessRole.Manager, true)]
    [InlineData(BusinessRole.Tailor, false)]
    public async Task Load_SetsPaymentActionVisibility_ByRole(string role, bool expected)
    {
        var vm = NewVm(role);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(expected, vm.CanManagePayments);
    }

    [Fact]
    public async Task RecordPayment_Tailor_DoesNotNavigate()
    {
        var vm = NewVm(BusinessRole.Tailor);
        var routes = new List<string>();
        vm.NavigateTo = r => { routes.Add(r); return Task.CompletedTask; };
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.RecordPaymentCommand.ExecuteAsync(null);

        Assert.Empty(routes);
    }

    [Theory]
    [InlineData(BusinessRole.Owner)]
    [InlineData(BusinessRole.Manager)]
    public async Task RecordPayment_OwnerOrManager_OpensPaymentsTab(string role)
    {
        var vm = NewVm(role);
        var routes = new List<string>();
        vm.NavigateTo = r => { routes.Add(r); return Task.CompletedTask; };
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.RecordPaymentCommand.ExecuteAsync(null);

        Assert.Equal(["//main/PaymentsPage"], routes);
    }

    [Fact]
    public async Task RecordPayment_MissingRoute_DoesNotThrow_AndHidesAction()
    {
        var vm = NewVm(BusinessRole.Owner);
        vm.NavigateTo = r => throw new ArgumentException($"unable to figure out route for: {r}", "uri");
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.RecordPaymentCommand.ExecuteAsync(null);

        Assert.False(vm.CanManagePayments);
    }

    private static HomeViewModel NewVm(string role) => new(
        new StubAuthService(new AuthUserDto("u1", "a@b.dev", "Ama", "Owusu", "b1", "Ama Stitches", role)),
        new StubCustomerLocalService(),
        new StubOrderLocalService());

    private sealed class StubAuthService(AuthUserDto? user = null) : IAuthService
    {
        public Task<AuthUserDto?> RegisterBusinessAsync(RegisterBusinessRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<AuthUserDto?>(null);

        public Task<AuthUserDto?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<AuthUserDto?>(null);

        public Task<TokenRefreshResult> RefreshAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasStoredTokensAsync() => Task.FromResult(false);

        public Task ClearTokensAsync() => Task.CompletedTask;

        public Task<bool> LogoutAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>(null);

        public Task StoreTokensAsync(TokenResponse tokens) => Task.CompletedTask;

        public Task StoreSessionAsync(AuthUserDto user) => Task.CompletedTask;

        public Task<string?> GetBusinessIdAsync() => Task.FromResult<string?>(null);

        public Task<AuthUserDto?> GetCachedUserAsync() => Task.FromResult(user);
    }

    private sealed class StubCustomerLocalService : ICustomerLocalService
    {
        public Task<IReadOnlyList<CustomerListItemDto>> SearchAsync(
            string businessId,
            string? search,
            int take = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerListItemDto>>(Array.Empty<CustomerListItemDto>());

        public Task<IReadOnlyList<CustomerListItemDto>> RecentAsync(
            string businessId,
            int take = 8,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerListItemDto>>(Array.Empty<CustomerListItemDto>());

        public Task<IReadOnlyList<CustomerListItemDto>> FindByPhoneAsync(
            string businessId,
            string? phone,
            string? excludeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerListItemDto>>([]);

        public Task<CustomerDetailDto?> GetByIdAsync(
            string businessId,
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CustomerDetailDto?>(null);

        public Task<RecordResult<CustomerDetailDto>> SaveAsync(
            string businessId,
            CreateCustomerRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RecordResult<CustomerDetailDto>> UpdateAsync(
            string businessId,
            string id,
            UpdateCustomerRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<NeedleBase.Application.Abstractions.CustomerDeleteResult> DeleteAsync(
            string businessId,
            string id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubOrderLocalService : IOrderLocalService
    {
        public Task<IReadOnlyList<OrderListItemDto>> SearchAsync(
            string businessId,
            string? search,
            string? status,
            int take = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderListItemDto>>(Array.Empty<OrderListItemDto>());

        public Task<OrderDetailDto?> GetByIdAsync(
            string businessId,
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OrderDetailDto?>(null);

        public Task<OrderCountsDto> GetCountsAsync(
            string businessId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new OrderCountsDto(0, 0, OrderStatus.All.ToDictionary(s => s, _ => 0)));

        public Task<IReadOnlyList<OrderListItemDto>> ListAttentionAsync(
            string businessId,
            int take = 5,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderListItemDto>>(Array.Empty<OrderListItemDto>());

        public Task<RecordResult<OrderDetailDto>> SaveAsync(
            string businessId,
            CreateOrderRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RecordResult<OrderDetailDto>> UpdateStatusAsync(
            string businessId,
            string id,
            string status,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<RecordResult<OrderDetailDto>> UpdateAsync(
            string businessId,
            string id,
            UpdateOrderRequest request,
            string? role,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
