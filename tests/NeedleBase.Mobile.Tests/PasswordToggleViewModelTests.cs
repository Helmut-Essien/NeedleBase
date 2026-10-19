using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

public class PasswordToggleViewModelTests
{
    [Fact]
    public void LoginViewModel_TogglePasswordVisibility_FlipsFlagAndDescription()
    {
        var vm = new LoginViewModel(new StubAuthService(), new StubNavigationService());

        Assert.False(vm.IsPasswordVisible);
        Assert.True(vm.IsPasswordHidden);
        Assert.Equal("Show password", vm.PasswordToggleDescription);

        vm.TogglePasswordVisibilityCommand.Execute(null);

        Assert.True(vm.IsPasswordVisible);
        Assert.False(vm.IsPasswordHidden);
        Assert.Equal("Hide password", vm.PasswordToggleDescription);

        vm.TogglePasswordVisibilityCommand.Execute(null);

        Assert.False(vm.IsPasswordVisible);
        Assert.True(vm.IsPasswordHidden);
        Assert.Equal("Show password", vm.PasswordToggleDescription);
    }

    [Fact]
    public void RegisterViewModel_PasswordToggles_AreIndependent()
    {
        var vm = new RegisterViewModel(new StubAuthService(), new StubNavigationService());

        Assert.Equal("Show password", vm.PasswordToggleDescription);
        Assert.Equal("Show confirm password", vm.ConfirmPasswordToggleDescription);

        vm.TogglePasswordVisibilityCommand.Execute(null);

        Assert.True(vm.IsPasswordVisible);
        Assert.False(vm.IsConfirmPasswordVisible);
        Assert.Equal("Hide password", vm.PasswordToggleDescription);
        Assert.Equal("Show confirm password", vm.ConfirmPasswordToggleDescription);

        vm.ToggleConfirmPasswordVisibilityCommand.Execute(null);

        Assert.True(vm.IsPasswordVisible);
        Assert.True(vm.IsConfirmPasswordVisible);
        Assert.Equal("Hide confirm password", vm.ConfirmPasswordToggleDescription);
    }

    [Fact]
    public void LoginViewModel_PrepareForDisplay_RemasksWithoutClearingPassword()
    {
        var vm = new LoginViewModel(new StubAuthService(), new StubNavigationService())
        {
            Password = "Secret1",
            IsPasswordVisible = true,
            ErrorMessage = "Invalid email or password."
        };

        vm.PrepareForDisplay();

        Assert.Equal("Secret1", vm.Password);
        Assert.False(vm.IsPasswordVisible);
        Assert.Equal("Invalid email or password.", vm.ErrorMessage);
    }

    [Fact]
    public void LoginViewModel_ClearSensitiveFields_WipesPassword()
    {
        var vm = new LoginViewModel(new StubAuthService(), new StubNavigationService())
        {
            Password = "Secret1",
            IsPasswordVisible = true,
            ErrorMessage = "Invalid email or password."
        };

        vm.ClearSensitiveFields();

        Assert.Equal(string.Empty, vm.Password);
        Assert.False(vm.IsPasswordVisible);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal("Show password", vm.PasswordToggleDescription);
    }

    [Fact]
    public void RegisterViewModel_PrepareForDisplay_RemasksWithoutClearingPasswords()
    {
        var vm = new RegisterViewModel(new StubAuthService(), new StubNavigationService())
        {
            Password = "Secret1",
            ConfirmPassword = "Secret1",
            IsPasswordVisible = true,
            IsConfirmPasswordVisible = true
        };

        vm.PrepareForDisplay();

        Assert.Equal("Secret1", vm.Password);
        Assert.Equal("Secret1", vm.ConfirmPassword);
        Assert.False(vm.IsPasswordVisible);
        Assert.False(vm.IsConfirmPasswordVisible);
    }

    [Fact]
    public void RegisterViewModel_ClearSensitiveFields_WipesPasswords()
    {
        var vm = new RegisterViewModel(new StubAuthService(), new StubNavigationService())
        {
            Password = "Secret1",
            ConfirmPassword = "Secret1",
            IsPasswordVisible = true
        };

        vm.ClearSensitiveFields();

        Assert.Equal(string.Empty, vm.Password);
        Assert.Equal(string.Empty, vm.ConfirmPassword);
        Assert.False(vm.IsPasswordVisible);
    }


    [Fact]
    public async Task LoginViewModel_LoginAsync_RemasksBeforeBusyWork()
    {
        var vm = new LoginViewModel(new StubAuthService(), new StubNavigationService())
        {
            Email = "ama@stitches.gh",
            Password = "Secret1",
            IsPasswordVisible = true
        };

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.False(vm.IsPasswordVisible);
        Assert.True(vm.IsPasswordHidden);
    }

    [Fact]
    public void LoginViewModel_RemaskPassword_HidesWithoutClearing()
    {
        var vm = new LoginViewModel(new StubAuthService(), new StubNavigationService())
        {
            Password = "Secret1",
            IsPasswordVisible = true
        };

        vm.RemaskPassword();

        Assert.Equal("Secret1", vm.Password);
        Assert.False(vm.IsPasswordVisible);
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

    private sealed class StubNavigationService : INavigationService
    {
        public Task ShowLoginAsync() => Task.CompletedTask;

        public Task ShowMainAsync() => Task.CompletedTask;
    }
}
