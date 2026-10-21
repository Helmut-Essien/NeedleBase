using NeedleBase.Application.Contracts.Errors;
using NeedleBase.Application.Common;
using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NeedleBase.Application.Contracts.Auth;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Measurements;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Domain.Common;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Configuration;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

/// <summary>Regression tests for the third edge-case audit fixes (device side).</summary>
public partial class SyncEdgeCaseTests
{
    // ---------------------------------------------------------------- phones (+233 form, format, duplicates)

    [Fact]
    public async Task Customer_Phone_IsNormalised_AndBadFormatRejected_OnlyWhenChanged()
    {
        await using var device = await DeviceAsync();
        var customers = device.GetRequiredService<ICustomerLocalService>();

        var (c, error) = await customers.SaveAsync(Biz, new CreateCustomerRequest("Efua", "024 555 0123", null, null));
        Assert.Null(error);
        Assert.Equal("+233245550123", c!.Phone);

        var bad = await customers.SaveAsync(Biz, new CreateCustomerRequest("Kofi", "call me", null, null));
        Assert.Equal(RecordError.PhoneInvalid, bad.Error);
        Assert.Equal(RecordErrorMessages.PhoneInvalid, bad.Code);
        Assert.Equal(PhoneNumbers.FormatMessage, bad.Message);

        // An old customer saved before the rule keeps editable notes; changing the number to a bad one is refused.
        await using (var scope = device.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Customers\" SET \"Phone\" = 'ask at shop' WHERE \"Id\" = {c.Id}");
        }

        var (kept, notesError) = await customers.UpdateAsync(Biz, c.Id, new UpdateCustomerRequest("Efua", "ask at shop", "new notes", null));
        Assert.Null(notesError);
        Assert.Equal("new notes", kept!.Notes);
        var changed = await customers.UpdateAsync(Biz, c.Id, new UpdateCustomerRequest("Efua", "12", null, null));
        Assert.Equal(RecordError.PhoneInvalid, changed.Error);
    }

    [Fact]
    public async Task Customer_FindByPhone_MatchesAnyFormatting()
    {
        await using var device = await DeviceAsync();
        var customers = device.GetRequiredService<ICustomerLocalService>();
        var id = await AddCustomerAsync(device, "Ama Duplicate");     // "0245550123"

        var matches = await customers.FindByPhoneAsync(Biz, "+233 24 555 0123", null);
        Assert.Equal(id, Assert.Single(matches).Id);
        Assert.Empty(await customers.FindByPhoneAsync(Biz, "0245550123", id));   // editing itself: no warning
        Assert.Empty(await customers.FindByPhoneAsync(Biz, "0245550124", null));
    }

    [Fact]
    public async Task Migrator_V7_NormalisesExistingPhones_AndKeys()
    {
        await using var device = await DeviceAsync();
        var id = await AddCustomerAsync(device, "Old Phone");
        var queued = (await QueueAsync(device)).Select(q => (q.EntityId, q.PayloadJson)).ToList();
        await using (var scope = device.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Customers\" SET \"Phone\" = '233 (0)20 111 2233', \"PhoneDigits\" = 'x' WHERE \"Id\" = {id}");
            await db.Database.ExecuteSqlRawAsync("PRAGMA user_version = 6;");
            Assert.Equal(LocalSchemaMigrator.LatestVersion, await LocalSchemaMigrator.MigrateAsync(db));
        }

        await using var check = device.CreateAsyncScope();
        var row = await check.ServiceProvider.GetRequiredService<LocalDbContext>().Customers.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.Phone, Digits = EF.Property<string>(c, LocalDbContext.PhoneDigitsColumn) })
            .SingleAsync();
        Assert.Equal("+233201112233", row.Phone);
        Assert.Equal("201112233", row.Digits);
        // Data step only: nothing new queued (the server normalises its own rows the same way).
        Assert.Equal(queued, (await QueueAsync(device)).Select(q => (q.EntityId, q.PayloadJson)).ToList());
    }

    // ---------------------------------------------------------------- old over-limit measurement stays editable

    [Fact]
    public async Task Measurement_OldOverLimit_NotesEditable_ChangedValueStillChecked()
    {
        await using var device = await DeviceAsync();
        var customerId = await AddCustomerAsync(device, "Legacy Size");
        var measurementId = await AddMeasurementAsync(device, customerId);
        await using (var scope = device.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"MeasurementProfiles\" SET \"FieldsJson\" = '{{\"chest\":400}}' WHERE \"Id\" = {measurementId}");
        }

        var service = device.GetRequiredService<IMeasurementLocalService>();
        CreateMeasurementRequest Request(decimal chest) => new()
        {
            Id = measurementId,
            GarmentTemplate = GarmentTemplate.Shirt,
            Unit = MeasurementUnit.Centimetres,
            Fields = new Dictionary<string, decimal?> { ["chest"] = chest },
            Notes = "edited"
        };

        var (kept, error) = await service.SaveAsync(Biz, customerId, Request(400m));
        Assert.Null(error);
        Assert.Equal("edited", kept!.Notes);
        var (_, changed) = await service.SaveAsync(Biz, customerId, Request(401m));
        Assert.NotNull(changed);
    }

    // ---------------------------------------------------------------- access revoked → wipe

    [Fact]
    public async Task Refresh_AccountDeactivated_SetsRevokedFlag_WipeClearsDataAndFlag()
    {
        var storage = new MemoryStorage();
        await storage.SetAsync(AuthService.AccessTokenKey, "a");
        await storage.SetAsync(AuthService.RefreshTokenKey, "r");
        await storage.SetAsync(AuthService.LocalDataOwnerKey, "U1|B1");
        var database = new CountingDatabase();
        var body = """{"status":401,"code":"ACCOUNT_DEACTIVATED"}""";
        var auth = new AuthService(new StubFactory(new StubHandler(HttpStatusCode.Unauthorized, body)), storage, database, Options.Create(new ApiSettings()));

        var result = await auth.RefreshAsync();

        Assert.False(result.Succeeded);
        Assert.True(result.AccessRevoked);
        Assert.Equal(AccessRevokedCodes.AccountDeactivated, await auth.GetAccessRevokedAsync());
        Assert.Equal("B1", await auth.GetLocalDataBusinessIdAsync());   // still known after tokens are cleared
        Assert.Equal(0, database.Wipes);                                  // nothing wiped until the user is told

        Assert.True(await auth.WipeRevokedAccessAsync());
        Assert.Equal(1, database.Wipes);
        Assert.Null(await auth.GetAccessRevokedAsync());
        Assert.Null(await storage.GetAsync(AuthService.LocalDataOwnerKey));
    }

    [Fact]
    public async Task Refresh_PlainExpiry_DoesNotSetRevokedFlag()
    {
        var storage = new MemoryStorage();
        await storage.SetAsync(AuthService.RefreshTokenKey, "r");
        var auth = new AuthService(new StubFactory(new StubHandler(HttpStatusCode.Unauthorized, """{"code":"INVALID_TOKEN"}""")),
            storage, new CountingDatabase(), Options.Create(new ApiSettings()));

        var result = await auth.RefreshAsync();

        Assert.False(result.AccessRevoked);
        Assert.Null(await auth.GetAccessRevokedAsync());
    }

    [Fact]
    public async Task Service_SessionExpiredWithRevokedFlag_RaisesAccessRevoked_AndCountsUnsynced()
    {
        var api = new CountingApi { Throw = () => new SyncSessionExpiredException(), ThrowOnPush = true };
        await using var device = await DeviceAsync();
        await AddCustomerAsync(device, "Unsynced");
        var service = new SyncService(Engine(device, api), new RevokedAuth(), new FakeConnectivity());
        var raised = 0;
        service.AccessRevoked += (_, _) => raised++;

        await service.RunAsync();

        Assert.Equal(1, raised);
        Assert.Equal(1, await service.PendingCountForAsync(Biz));
        Assert.Contains("1 change(s)", AccessRevokedService.Message(AccessRevokedCodes.NoMembership, 1));
        Assert.DoesNotContain("change(s)", AccessRevokedService.Message(AccessRevokedCodes.AccountDeactivated, 0));
    }

    // ---------------------------------------------------------------- sync triggers

    [Fact]
    public async Task Service_Foreground_TriggersRun_ThrottledAndNotWhileStopped()
    {
        var api = new CountingApi();
        await using var device = await DeviceAsync();
        var now = DateTimeOffset.UtcNow;
        var service = new SyncService(Engine(device, api), new FakeAuth(), new FakeConnectivity(), () => now);
        service.Start();
        await WaitUntilAsync(() => api.Pulls >= 1);

        service.OnForeground();
        await WaitUntilAsync(() => api.Pulls >= 2);
        service.OnForeground();                       // a dialog closing re-activates: throttled
        await Task.Delay(300);
        Assert.Equal(2, api.Pulls);

        now += TimeSpan.FromMinutes(1);
        await service.StopAsync();
        service.OnForeground();
        await Task.Delay(300);
        Assert.Equal(2, api.Pulls);
        service.OnBackground();
    }

    [Fact]
    public async Task PullToRefresh_SyncsBeforeReloading()
    {
        var api = new CountingApi();
        await using var device = await DeviceAsync();
        var service = new SyncService(Engine(device, api), new FakeAuth(), new FakeConnectivity());
        service.Start();
        await WaitUntilAsync(() => api.Pulls >= 1);
        var before = api.Pulls;

        var vm = new CustomersViewModel(device.GetRequiredService<ICustomerLocalService>(), new FakeAuth(), service);
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.True(api.Pulls > before);
        await service.StopAsync();
    }

    // ---------------------------------------------------------------- money format, back guard

    [Fact]
    public void OrderLine_TotalDisplay_IsEnGh_WhateverTheDeviceCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            var line = new OrderLineDto("L1", GarmentTemplate.Shirt, 1, 1250m, 1250m, null, MeasurementUnit.Centimetres,
                new Dictionary<string, decimal?>(), null, 0);
            Assert.Equal("GH₵ 1,250.00 · No measurements", line.LineTotalDisplay);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void EditViewModels_AreBackAware()
    {
        Assert.True(typeof(IBackAware).IsAssignableFrom(typeof(CustomerEditViewModel)));
        Assert.True(typeof(IBackAware).IsAssignableFrom(typeof(MeasurementEditViewModel)));
        Assert.True(typeof(IBackAware).IsAssignableFrom(typeof(CreateOrderViewModel)));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(50);
        }

        Assert.True(condition());
    }

    private sealed class RevokedAuth : IAuthService
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
        public Task<AuthUserDto?> GetCachedUserAsync() => Task.FromResult<AuthUserDto?>(null);
        public Task<string?> GetAccessRevokedAsync() => Task.FromResult<string?>(AccessRevokedCodes.NoMembership);
    }
}
