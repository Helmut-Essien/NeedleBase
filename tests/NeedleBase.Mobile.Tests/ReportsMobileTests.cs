using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeedleBase.Application.Common;
using NeedleBase.Application.Contracts.Customers;
using NeedleBase.Application.Contracts.Licensing;
using NeedleBase.Application.Contracts.Orders;
using NeedleBase.Application.Contracts.Payments;
using NeedleBase.Application.Reports;
using NeedleBase.Domain.Entities;
using NeedleBase.Domain.Enums;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.ViewModels;

namespace NeedleBase.Mobile.Tests;

/// <summary>Plan step 7b on the phone: reports from local SQLite (same maths as the server) and the plan/role gate.</summary>
[Collection(LocalDataVersionCollection.Name)] // records payments (bumps the local data version other tests count)
public class ReportsMobileTests
{
    private const string Biz = "biz-rep";
    private const string Other = "biz-rep-other";

    [Fact]
    public async Task Local_report_matches_server_rules_offline()
    {
        await using var device = await DeviceAsync();
        var today = BusinessTime.Today(DateTimeOffset.UtcNow);
        var month = ReportRules.FirstOfMonth(today);
        var payments = device.GetRequiredService<IPaymentLocalService>();

        var partPaid = await AddOrderAsync(device, Biz, 300m);
        var paid = await AddOrderAsync(device, Biz, 200m);
        var cancelled = await AddOrderAsync(device, Biz, 999m);
        await device.GetRequiredService<IOrderLocalService>().UpdateStatusAsync(Biz, cancelled, OrderStatus.Cancelled);
        Assert.True((await payments.RecordAsync(Biz, partPaid, new RecordPaymentRequest { Amount = 100m, Method = PaymentMethod.MoMo })).IsSuccess);
        Assert.True((await payments.RecordAsync(Biz, paid, new RecordPaymentRequest { Amount = 200m, Method = PaymentMethod.Cash })).IsSuccess);
        var voided = await payments.RecordAsync(Biz, partPaid, new RecordPaymentRequest { Amount = 50m, Method = PaymentMethod.Cash });
        Assert.True((await payments.VoidAsync(Biz, voided.Value!.Id, "typo")).IsSuccess);

        // Last month: an older order with a payment; another shop's money never shows.
        var lastMonthStart = BusinessTime.StartOfDayUtc(month.AddMonths(-1));
        var old = await AddOrderAsync(device, Biz, 80m);
        await BackdateAsync(device, old, lastMonthStart.AddHours(9), 30m, lastMonthStart.AddHours(10));
        var foreign = await AddOrderAsync(device, Other, 5000m);
        Assert.True((await payments.RecordAsync(Other, foreign, new RecordPaymentRequest { Amount = 5000m, Method = PaymentMethod.Cash })).IsSuccess);

        var reports = device.GetRequiredService<IReportLocalService>();
        var r = await reports.GetAsync(Biz, month, today);
        Assert.Equal(300m, r.MoneyReceived); // voided excluded, includes payments still waiting to sync
        Assert.Equal(2, r.PaymentCount);
        Assert.Equal(30m, r.PreviousMoneyReceived);
        Assert.Equal((2, 500m, 200m, 1, 1), (r.OrdersTaken, r.OrdersTakenValue, r.StillOwed, r.OrdersStillOwing, r.OrdersCancelled));
        Assert.Equal([(PaymentMethod.Cash, 200m), (PaymentMethod.MoMo, 100m)], r.ByMethod.Select(m => (m.Method, m.Amount)));
        Assert.True(r.IsCurrentMonth);

        var earlier = await reports.GetAsync(Biz, month.AddMonths(-1), today);
        Assert.Equal((30m, 1, 50m), (earlier.MoneyReceived, earlier.OrdersTaken, earlier.StillOwed));

        var empty = await reports.GetAsync(Biz, ReportRules.EarliestMonth, today);
        Assert.True(empty.IsEmpty);
    }

    [Fact]
    public async Task Empty_phone_gives_zero_report()
    {
        await using var device = await DeviceAsync();
        var today = BusinessTime.Today(DateTimeOffset.UtcNow);
        var r = await device.GetRequiredService<IReportLocalService>().GetAsync(Biz, today, today);
        Assert.True(r.IsEmpty);
        Assert.Equal(ReportRules.TrendMonths, r.Trend.Count);
    }

    [Theory]
    [InlineData(true, BusinessRole.Owner, true, ReportsViewModel.FreeMessage)]
    [InlineData(false, BusinessRole.Tailor, true, ReportRules.ForbiddenMessage)]
    [InlineData(false, null, true, ReportRules.ForbiddenMessage)]
    [InlineData(false, BusinessRole.Owner, false, ReportRules.NotAllowedMessage)]
    [InlineData(false, BusinessRole.Manager, false, ReportRules.NotAllowedMessage)]
    [InlineData(false, BusinessRole.Owner, true, null)]
    [InlineData(false, BusinessRole.Manager, true, null)]
    public void Gate_by_free_role_and_plan(bool isFree, string? role, bool canReports, string? expected) =>
        Assert.Equal(expected, ReportsViewModel.Gate(isFree, role, new SubscriptionStatusDto { CanReports = canReports }));

    [Fact]
    public void Gate_stays_closed_without_a_status_and_opens_for_unlimited()
    {
        Assert.Equal(ReportRules.NotAllowedMessage, ReportsViewModel.Gate(false, BusinessRole.Owner, null));
        Assert.Null(ReportsViewModel.Gate(false, BusinessRole.Owner, SubscriptionStatusDto.Unlocked));
    }

    [Fact]
    public async Task Load_invalid_month_shows_message_without_building()
    {
        NeedleBase.Mobile.Data.Sync.LocalSubscriptionGate.Clear();
        try
        {
            NeedleBase.Mobile.Data.Sync.LocalSubscriptionGate.Set(Biz, SubscriptionStatusDto.Unlocked);
            var reports = new CountingReports();
            var vm = new ReportsViewModel(new OwnerAuth(), reports)
            {
                UtcNow = () => new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero)
            };
            await vm.LoadAsync("2099-01");
            Assert.Null(vm.Report);
            Assert.Equal(ReportRules.MonthInvalidMessage, vm.Message);
            Assert.Equal(0, reports.Calls);
        }
        finally
        {
            NeedleBase.Mobile.Data.Sync.LocalSubscriptionGate.Clear();
        }
    }

    private sealed class CountingReports : IReportLocalService
    {
        public int Calls { get; private set; }

        public Task<NeedleBase.Application.Contracts.Reports.MonthlyReportDto> GetAsync(
            string businessId, DateOnly month, DateOnly today, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("report must not be built for an invalid month");
        }
    }

    private sealed class OwnerAuth : NeedleBase.Mobile.Services.IAuthService
    {
        public Task<NeedleBase.Application.Contracts.Auth.AuthUserDto?> RegisterBusinessAsync(
            NeedleBase.Application.Contracts.Auth.RegisterBusinessRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<NeedleBase.Application.Contracts.Auth.AuthUserDto?>(null);

        public Task<NeedleBase.Application.Contracts.Auth.AuthUserDto?> LoginAsync(
            NeedleBase.Application.Contracts.Auth.LoginRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<NeedleBase.Application.Contracts.Auth.AuthUserDto?>(null);

        public Task<NeedleBase.Mobile.Services.TokenRefreshResult> RefreshAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasStoredTokensAsync() => Task.FromResult(true);
        public Task ClearTokensAsync() => Task.CompletedTask;
        public Task<bool> LogoutAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("t");
        public Task StoreTokensAsync(NeedleBase.Application.Contracts.Auth.TokenResponse tokens) => Task.CompletedTask;
        public Task StoreSessionAsync(NeedleBase.Application.Contracts.Auth.AuthUserDto user) => Task.CompletedTask;
        public Task<string?> GetBusinessIdAsync() => Task.FromResult<string?>(Biz);

        public Task<NeedleBase.Application.Contracts.Auth.AuthUserDto?> GetCachedUserAsync() =>
            Task.FromResult<NeedleBase.Application.Contracts.Auth.AuthUserDto?>(
                new("u1", "owner@ama-stitches.test", "Ama", "Owusu", Biz, "Ama Stitches", BusinessRole.Owner));
    }

    private static async Task BackdateAsync(ServiceProvider device, string orderId, DateTimeOffset createdAtUtc, decimal paid, DateTimeOffset paidAtUtc)
    {
        await using var scope = device.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LocalDbContext>();
        var order = await db.Orders.SingleAsync(o => o.Id == orderId);
        db.Entry(order).Property(o => o.CreatedAtUtc).CurrentValue = createdAtUtc;
        db.Payments.Add(Payment.Create(IdGenerator.NewId(), Biz, orderId, order.CustomerId, paid, PaymentMethod.Cash, null, paidAtUtc, paidAtUtc));
        await db.SaveChangesAsync();
    }

    private static async Task<string> AddOrderAsync(ServiceProvider device, string businessId, decimal total)
    {
        var (c, error) = await device.GetRequiredService<ICustomerLocalService>()
            .SaveAsync(businessId, new CreateCustomerRequest("Efua Mensah", "0245550123", null, null));
        Assert.Null(error);
        var (o, orderError) = await device.GetRequiredService<IOrderLocalService>().SaveAsync(businessId, new CreateOrderRequest
        {
            CustomerId = c!.Id,
            TotalAmount = total,
            Lines =
            [
                new CreateOrderLineRequest
                {
                    GarmentTemplate = GarmentTemplate.Dress, Quantity = 1, UnitPrice = total,
                    Unit = MeasurementUnit.Centimetres, MeasurementFields = new Dictionary<string, decimal?>()
                }
            ]
        });
        Assert.Null(orderError);
        return o!.Id;
    }

    private static async Task<ServiceProvider> DeviceAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"needlebase-rep-{Guid.NewGuid():N}.db3");
        var services = new ServiceCollection();
        services.AddDbContext<LocalDbContext>(o => o.UseSqlite($"Data Source={path};Pooling=False"));
        services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
        services.AddSingleton<IOrderLocalService, OrderLocalService>();
        services.AddSingleton<IPaymentLocalService, PaymentLocalService>();
        services.AddSingleton<IReportLocalService, ReportLocalService>();
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await LocalSchemaMigrator.MigrateAsync(scope.ServiceProvider.GetRequiredService<LocalDbContext>());
        return provider;
    }
}
