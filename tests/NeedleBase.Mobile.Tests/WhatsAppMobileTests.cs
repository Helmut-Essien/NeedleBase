using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.WhatsApp;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

public class WhatsAppMobileTests
{
    private const string Biz = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        var dbPath = Path.Combine(Path.GetTempPath(), $"needlebase-wa-{Guid.NewGuid():N}.db3");
        services.AddSingleton(new LocalDbOptions { DbPath = dbPath });
        services.AddDbContext<LocalDbContext>(o => o.UseSqlite($"Data Source={dbPath};Cache=Shared"));
        services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
        services.AddSingleton<WhatsAppConsentSync>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        LocalSchemaMigrator.MigrateAsync(scope.ServiceProvider.GetRequiredService<LocalDbContext>()).GetAwaiter().GetResult();
        return provider;
    }

    private sealed class FakeConsentApi : IWhatsAppConsentApi
    {
        public Func<string, bool, DateTimeOffset, ConsentPushResult> Set { get; set; } =
            (id, on, at) => new ConsentPushResult(ConsentPushOutcome.Saved, new WhatsAppConsentDto(id, on, "staff", at));

        public int Calls { get; private set; }

        public Task<ConsentPushResult> GetConsentAsync(string customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConsentPushResult(ConsentPushOutcome.Offline));

        public Task<ConsentPushResult> SetConsentAsync(string customerId, bool optedIn, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Set(customerId, optedIn, changedAtUtc));
        }
    }

    [Fact]
    public async Task OfflineChoice_StaysPending_ThenPushesWithItsTime()
    {
        await using var p = Provider();
        var consent = p.GetRequiredService<WhatsAppConsentSync>();
        var view = await consent.SetAsync(Biz, "c1", true, T0);
        Assert.True(view.OptedIn);
        Assert.True(view.Pending);

        var api = new FakeConsentApi { Set = (_, _, _) => new ConsentPushResult(ConsentPushOutcome.Offline) };
        Assert.Equal(0, await consent.PushAsync(Biz, api));
        Assert.True((await consent.GetAsync(Biz, "c1")).Pending);

        DateTimeOffset? sentAt = null;
        api.Set = (id, on, at) =>
        {
            sentAt = at;
            return new ConsentPushResult(ConsentPushOutcome.Saved, new WhatsAppConsentDto(id, on, "staff", at));
        };
        Assert.Equal(1, await consent.PushAsync(Biz, api));
        Assert.Equal(T0, sentAt);
        var after = await consent.GetAsync(Biz, "c1");
        Assert.True(after.OptedIn);
        Assert.False(after.Pending);
        Assert.Equal(0, await consent.PendingCountAsync(Biz));
    }

    [Fact]
    public async Task ServerStopReply_Wins_OverALateOfflineOptIn()
    {
        await using var p = Provider();
        var consent = p.GetRequiredService<WhatsAppConsentSync>();
        await consent.SetAsync(Biz, "c1", true, T0);
        var api = new FakeConsentApi { Set = (id, _, _) => new ConsentPushResult(ConsentPushOutcome.Saved, new WhatsAppConsentDto(id, false, "reply", T0.AddMinutes(5))) };
        await consent.PushAsync(Biz, api);
        var view = await consent.GetAsync(Biz, "c1");
        Assert.False(view.OptedIn);
        Assert.False(view.Pending);
        Assert.True(view.RepliedStop);
        Assert.Contains("STOP", CustomerDetailViewModel.DescribeConsent(view));
    }

    [Fact]
    public async Task ServerRefresh_KeepsANewerPendingChoice_DropsAnOlderOne()
    {
        await using var p = Provider();
        var consent = p.GetRequiredService<WhatsAppConsentSync>();
        await consent.SetAsync(Biz, "c1", true, T0);
        // Server state older than the pending choice: pending kept.
        var v1 = await consent.ApplyServerAsync(Biz, new WhatsAppConsentDto("c1", false, null, null));
        Assert.True(v1.Pending);
        // Server changed after the choice (another phone / STOP): server wins.
        var v2 = await consent.ApplyServerAsync(Biz, new WhatsAppConsentDto("c1", false, "reply", T0.AddMinutes(1)));
        Assert.False(v2.Pending);
        Assert.True(v2.RepliedStop);
    }

    [Fact]
    public async Task ChoosingTheServerValueAgain_ClearsThePendingChange()
    {
        await using var p = Provider();
        var consent = p.GetRequiredService<WhatsAppConsentSync>();
        await consent.ApplyServerAsync(Biz, new WhatsAppConsentDto("c1", true, "staff", T0));
        Assert.True((await consent.SetAsync(Biz, "c1", false, T0.AddMinutes(1))).Pending);
        var back = await consent.SetAsync(Biz, "c1", true, T0.AddMinutes(2));
        Assert.False(back.Pending);
        var api = new FakeConsentApi();
        Assert.Equal(0, await consent.PushAsync(Biz, api));
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public async Task NotFound_KeepsPendingForUnsyncedCustomer_DropsForDeletedOne_Forbidden_Drops()
    {
        await using var p = Provider();
        var consent = p.GetRequiredService<WhatsAppConsentSync>();
        var (customer, _) = await p.GetRequiredService<ICustomerLocalService>().SaveAsync(Biz, new CreateCustomerRequest("Efua Mensah", "+233245550123", null, null));
        await consent.SetAsync(Biz, customer!.Id, true, T0);
        await consent.SetAsync(Biz, "gone", true, T0);
        var api = new FakeConsentApi { Set = (_, _, _) => new ConsentPushResult(ConsentPushOutcome.NotFound) };
        await consent.PushAsync(Biz, api);
        Assert.True((await consent.GetAsync(Biz, customer.Id)).Pending);
        Assert.False((await consent.GetAsync(Biz, "gone")).Pending); // row removed
        Assert.Equal(1, await consent.PendingCountAsync(Biz));

        api.Set = (_, _, _) => new ConsentPushResult(ConsentPushOutcome.Forbidden);
        await consent.PushAsync(Biz, api);
        Assert.Equal(0, await consent.PendingCountAsync(Biz));
    }

    [Fact]
    public async Task OtherBusiness_DoesNotSeeConsent()
    {
        await using var p = Provider();
        var consent = p.GetRequiredService<WhatsAppConsentSync>();
        await consent.SetAsync(Biz, "c1", true, T0);
        Assert.False((await consent.GetAsync("OTHERBIZ0000000000000000000", "c1")).OptedIn);
        await Assert.ThrowsAsync<InvalidOperationException>(() => consent.SetAsync("OTHERBIZ0000000000000000000", "c1", false, T0));
    }

    // ---- Settings screen ----

    private sealed class FakeAdmin : IWhatsAppAdminApi
    {
        public WhatsAppSettingsDto Settings { get; set; } = new(true, true, ["Ready"], ["Ready", "Collected"], 5, null);
        public List<(bool, IReadOnlyList<string>)> Saves { get; } = new();
        public bool Offline { get; set; }

        public Task<StaffCallResult<WhatsAppSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Offline ? new StaffCallResult<WhatsAppSettingsDto>(null, StaffApi.OfflineMessage, true) : StaffCallResult<WhatsAppSettingsDto>.Ok(Settings));

        public Task<StaffCallResult<WhatsAppSettingsDto>> SaveSettingsAsync(bool enabled, IReadOnlyList<string> statuses, CancellationToken cancellationToken = default)
        {
            Saves.Add((enabled, statuses));
            Settings = Settings with { Enabled = enabled, Statuses = statuses };
            return Task.FromResult(StaffCallResult<WhatsAppSettingsDto>.Ok(Settings));
        }

        public Task<StaffCallResult<WhatsAppMessagePageDto>> GetMessagesAsync(int page, CancellationToken cancellationToken = default) =>
            Task.FromResult(StaffCallResult<WhatsAppMessagePageDto>.Ok(new WhatsAppMessagePageDto(
                page == 1 ? [new WhatsAppMessageDto("m1", "o1", "ORD-1", "Efua", "+233•••123", "Ready", "Sent", 1, T0, T0, null)] : [],
                page, 1, page == 1)));
    }

    private sealed class Auth(string role) : IAuthService
    {
        public Task<AuthUserDto?> RegisterBusinessAsync(RegisterBusinessRequest request, CancellationToken cancellationToken = default) => Task.FromResult<AuthUserDto?>(null);
        public Task<AuthUserDto?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) => Task.FromResult<AuthUserDto?>(null);
        public Task<TokenRefreshResult> RefreshAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasStoredTokensAsync() => Task.FromResult(true);
        public Task ClearTokensAsync() => Task.CompletedTask;
        public Task<bool> LogoutAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("t");
        public Task StoreTokensAsync(TokenResponse tokens) => Task.CompletedTask;
        public Task StoreSessionAsync(AuthUserDto user) => Task.CompletedTask;
        public Task<string?> GetBusinessIdAsync() => Task.FromResult<string?>(Biz);
        public Task<AuthUserDto?> GetCachedUserAsync() => Task.FromResult<AuthUserDto?>(new("u1", "owner@ama-stitches.test", "Ama", "Owusu", Biz, "Ama Stitches", role));
    }

    [Fact]
    public async Task Settings_LoadsStatusesAndLog_SavesChosenStatuses()
    {
        var api = new FakeAdmin();
        var vm = new WhatsAppSettingsViewModel(api, new Auth(BusinessRole.Manager));
        await vm.LoadAsync();
        Assert.True(vm.Loaded);
        Assert.True(vm.CanSave);
        Assert.Equal(["Ready", "Collected"], vm.Statuses.Select(s => s.Status));
        Assert.Single(vm.Messages);
        Assert.True(vm.HasMore);
        await vm.LoadMoreCommand.ExecuteAsync(null);
        Assert.False(vm.HasMore);

        vm.Statuses[1].IsChecked = true;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(["Ready", "Collected"], api.Saves.Single().Item2);
        Assert.True(vm.HasStatus);
    }

    [Fact]
    public async Task Settings_OnWithNoStatus_IsRefused_Tailor_IsReadOnly_Offline_ShowsRetry()
    {
        var api = new FakeAdmin();
        var vm = new WhatsAppSettingsViewModel(api, new Auth(BusinessRole.Owner));
        await vm.LoadAsync();
        vm.Statuses[0].IsChecked = false;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Empty(api.Saves);
        Assert.True(vm.HasError);

        var tailor = new WhatsAppSettingsViewModel(api, new Auth(BusinessRole.Tailor));
        await tailor.LoadAsync();
        Assert.True(tailor.ReadOnly);
        Assert.False(tailor.CanSave);
        await tailor.SaveCommand.ExecuteAsync(null);
        Assert.Empty(api.Saves);

        var offline = new WhatsAppSettingsViewModel(new FakeAdmin { Offline = true }, new Auth(BusinessRole.Owner));
        await offline.LoadAsync();
        Assert.False(offline.Loaded);
        Assert.Equal(StaffApi.OfflineMessage, offline.ErrorMessage);
    }

    [Fact]
    public async Task Settings_NotConfiguredOnServer_CannotSave()
    {
        var api = new FakeAdmin { Settings = new WhatsAppSettingsDto(false, false, [], [], 5, null) };
        var vm = new WhatsAppSettingsViewModel(api, new Auth(BusinessRole.Owner));
        await vm.LoadAsync();
        Assert.True(vm.NotConfigured);
        Assert.False(vm.CanSave);
    }

    [Fact]
    public async Task ChangeMadeDuringAPush_IsNotLost()
    {
        await using var p = Provider();
        var consent = p.GetRequiredService<WhatsAppConsentSync>();
        await consent.SetAsync(Biz, "c1", true, T0);
        var api = new FakeConsentApi();
        api.Set = (id, on, at) =>
        {
            // While the "on" is on its way, someone turns it off again on this phone.
            consent.SetAsync(Biz, "c1", false, T0.AddMinutes(1)).GetAwaiter().GetResult();
            return new ConsentPushResult(ConsentPushOutcome.Saved, new WhatsAppConsentDto(id, on, "staff", at));
        };
        Assert.Equal(1, await consent.PushAsync(Biz, api));
        var view = await consent.GetAsync(Biz, "c1");
        Assert.True(view.Pending); // the newer "off" still goes out next
        Assert.False(view.OptedIn);

        api.Set = (id, on, at) => new ConsentPushResult(ConsentPushOutcome.Saved, new WhatsAppConsentDto(id, on, "staff", at));
        Assert.Equal(1, await consent.PushAsync(Biz, api));
        Assert.False((await consent.GetAsync(Biz, "c1")).Pending);
    }

    [Fact]
    public async Task SavingAChoice_RaisesChanged_ForTheSyncBadge()
    {
        await using var p = Provider();
        var consent = p.GetRequiredService<WhatsAppConsentSync>();
        var raised = 0;
        consent.Changed += (_, _) => raised++;
        await consent.SetAsync(Biz, "c1", true, T0);
        Assert.Equal(1, raised);
        Assert.Equal(1, await consent.PendingCountAsync(Biz));
    }

    [Fact]
    public async Task DoubleTap_AsksOnce()
    {
        await using var p = Provider();
        var consent = p.GetRequiredService<WhatsAppConsentSync>();
        var vm = new CustomerDetailViewModel(p.GetRequiredService<ICustomerLocalService>(), null!, new Auth(BusinessRole.Owner), consent, null);
        var asked = 0;
        var answer = new TaskCompletionSource<bool>();
        vm.Confirm = (_, _) => { asked++; return answer.Task; };
        typeof(CustomerDetailViewModel).GetProperty(nameof(CustomerDetailViewModel.CustomerId))!.SetValue(vm, "c1");
        vm.CanChangeConsent = true;
        vm.Consent = await consent.GetAsync(Biz, "c1");

        var first = vm.ToggleConsentCommand.ExecuteAsync(null);
        var second = vm.ToggleConsentCommand.ExecuteAsync(null);
        answer.SetResult(true);
        await Task.WhenAll(first, second);
        Assert.Equal(1, asked);
        Assert.True((await consent.GetAsync(Biz, "c1")).OptedIn);
    }

    [Fact]
    public async Task UpgradeFromV9_AddsConsentTable_AndRerunIsHarmless()
    {
        await using var p = Provider();
        using var scope = p.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE \"WhatsAppConsents\"; PRAGMA user_version = 9;");
        Assert.Equal(LocalSchemaMigrator.LatestVersion, await LocalSchemaMigrator.MigrateAsync(db));
        Assert.Equal(0, await db.WhatsAppConsents.CountAsync());
        Assert.Equal(LocalSchemaMigrator.LatestVersion, await LocalSchemaMigrator.MigrateAsync(db));
    }
}
