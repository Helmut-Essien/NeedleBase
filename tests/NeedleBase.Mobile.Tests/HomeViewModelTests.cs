using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

public class HomeViewModelTests
{
    [Fact]
    public void HomeViewModel_HasScaffoldTitle()
    {
        var vm = new HomeViewModel(new StubAuthService(), new StubCustomerLocalService());
        Assert.Equal("Home", vm.Title);
        Assert.False(string.IsNullOrWhiteSpace(vm.Tagline));
    }

    private sealed class StubAuthService : IAuthService
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

        public Task<AuthUserDto?> GetCachedUserAsync() => Task.FromResult<AuthUserDto?>(null);
    }

    private sealed class StubCustomerLocalService : ICustomerLocalService
    {
        public Task<IReadOnlyList<CustomerListItemDto>> SearchAsync(
            string businessId,
            string? search,
            int take = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerListItemDto>>(Array.Empty<CustomerListItemDto>());

        public Task<CustomerDetailDto?> GetByIdAsync(
            string businessId,
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CustomerDetailDto?>(null);

        public Task<(CustomerDetailDto? Customer, string? Error)> SaveAsync(
            string businessId,
            CreateCustomerRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(CustomerDetailDto? Customer, string? Error)> UpdateAsync(
            string businessId,
            string id,
            UpdateCustomerRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
