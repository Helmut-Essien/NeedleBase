using CommunityToolkit.Maui;
using MauiIcons.Material;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NeedleBase.Mobile.Configuration;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
using NeedleBase.Mobile.Data.Sync;
using NeedleBase.Mobile.Services;
using NeedleBase.Mobile.ViewModels;
using NeedleBase.Mobile.Views;

namespace NeedleBase.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMaterialMauiIcons()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.Configure<ApiSettings>(options =>
        {
            // DEBUG Android: loopback + `adb reverse tcp:5036 tcp:5036` (NeedleBase Api + Mobile).
            // Emulator without reverse: use http://10.0.2.2:5036/ instead.
#if DEBUG
            options.BaseUrl = "http://localhost:5036/";
#else
            // Release: from the build-time MSBuild property NeedleBaseReleaseApiBaseUrl
            // (placeholder .invalid host unless set; see NeedleBase.Mobile.csproj).
            options.BaseUrl = ApiEndpoint.ReleaseBaseUrl()
                ?? "https://" + ApiEndpoint.PlaceholderHost + "/";
#endif
        });

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "needlebase.db3");
        builder.Services.AddSingleton(new LocalDbOptions { DbPath = dbPath });
        builder.Services.AddDbContext<LocalDbContext>((sp, options) =>
        {
            var path = sp.GetRequiredService<LocalDbOptions>().DbPath;
            options.UseSqlite(NeedleBase.Mobile.Data.Data.LocalDbConnection.Build(path));
        });

        builder.Services.AddHttpClient("AuthClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        builder.Services.AddTransient<TokenRefreshHandler>();
        builder.Services.AddHttpClient(HttpSyncApi.ClientName, client => client.Timeout = TimeSpan.FromSeconds(60))
            .AddHttpMessageHandler<TokenRefreshHandler>();
        builder.Services.AddSingleton(Connectivity.Current);
        builder.Services.AddSingleton<ISyncApi, HttpSyncApi>();
        builder.Services.AddSingleton<SyncEngine>();
        builder.Services.AddSingleton<SyncService>();
        builder.Services.AddSingleton<SyncStatusViewModel>();
        builder.Services.AddTransient<SyncDetailsPage>();

        builder.Services.AddSingleton<ISecureStorageService, MauiSecureStorageService>();
        builder.Services.AddSingleton<IAuthService, AuthService>();
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<ILocalDatabaseService, LocalDatabaseService>();
        builder.Services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
        builder.Services.AddSingleton<IMeasurementLocalService, MeasurementLocalService>();
        builder.Services.AddSingleton<IOrderLocalService, OrderLocalService>();
        builder.Services.AddSingleton<IPaymentLocalService, PaymentLocalService>();
        builder.Services.AddSingleton<AppStartupService>();
        builder.Services.AddSingleton<AccessRevokedService>();

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<RegisterViewModel>();
        builder.Services.AddSingleton<HomeViewModel>();
        builder.Services.AddSingleton<CustomersViewModel>();
        builder.Services.AddSingleton<OrdersViewModel>();
        builder.Services.AddTransient<CustomerEditViewModel>();
        builder.Services.AddTransient<CustomerDetailViewModel>();
        builder.Services.AddTransient<MeasurementEditViewModel>();
        builder.Services.AddTransient<CreateOrderViewModel>();
        builder.Services.AddTransient<OrderDetailViewModel>();
        builder.Services.AddSingleton<PaymentsViewModel>();
        builder.Services.AddTransient<RecordPaymentViewModel>();
        builder.Services.AddTransient<EditOrderViewModel>();
        builder.Services.AddTransient<PaymentReceiptViewModel>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<RegisterPage>();
        builder.Services.AddTransient<TermsPage>();
        builder.Services.AddTransient<PrivacyPage>();
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddSingleton<CustomersPage>();
        builder.Services.AddSingleton<OrdersPage>();
        builder.Services.AddTransient<CustomerEditPage>();
        builder.Services.AddTransient<CustomerDetailPage>();
        builder.Services.AddTransient<MeasurementEditPage>();
        builder.Services.AddTransient<CreateOrderPage>();
        builder.Services.AddTransient<OrderDetailPage>();
        builder.Services.AddSingleton<PaymentsPage>();
        builder.Services.AddTransient<RecordPaymentPage>();
        builder.Services.AddTransient<EditOrderPage>();
        builder.Services.AddTransient<PaymentReceiptPage>();
        builder.Services.AddSingleton<MorePage>();
        builder.Services.AddSingleton<IStaffApi, StaffApi>();
        builder.Services.AddTransient<StaffViewModel>();
        builder.Services.AddTransient<StaffPage>();
        builder.Services.AddTransient<ChangePasswordViewModel>();
        builder.Services.AddTransient<ChangePasswordPage>();
        builder.Services.AddTransient<AcceptInviteViewModel>();
        builder.Services.AddTransient<AcceptInvitePage>();
        builder.Services.AddTransient<ForgotPasswordViewModel>();
        builder.Services.AddTransient<ForgotPasswordPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
