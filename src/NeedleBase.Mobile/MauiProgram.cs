using CommunityToolkit.Maui;
using MauiIcons.Material;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NeedleBase.Mobile.Configuration;
using NeedleBase.Mobile.Data.Data;
using NeedleBase.Mobile.Data.Services;
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
            // Android emulator → host machine. Override in appsettings / preferences later.
#if ANDROID
            options.BaseUrl = "https://10.0.2.2:7079/";
#else
            options.BaseUrl = "https://localhost:7079/";
#endif
        });

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "needlebase.db3");
        builder.Services.AddSingleton(new LocalDbOptions { DbPath = dbPath });
        builder.Services.AddDbContext<LocalDbContext>((sp, options) =>
        {
            var path = sp.GetRequiredService<LocalDbOptions>().DbPath;
            options.UseSqlite($"Data Source={path};Cache=Shared");
        });

        builder.Services.AddHttpClient("AuthClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        builder.Services.AddSingleton<ISecureStorageService, MauiSecureStorageService>();
        builder.Services.AddSingleton<IAuthService, AuthService>();
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<ILocalDatabaseService, LocalDatabaseService>();
        builder.Services.AddSingleton<ICustomerLocalService, CustomerLocalService>();
        builder.Services.AddSingleton<IMeasurementLocalService, MeasurementLocalService>();

        builder.Services.AddTransient<SplashViewModel>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<RegisterViewModel>();
        builder.Services.AddSingleton<HomeViewModel>();
        builder.Services.AddSingleton<CustomersViewModel>();
        builder.Services.AddTransient<CustomerEditViewModel>();
        builder.Services.AddTransient<CustomerDetailViewModel>();
        builder.Services.AddTransient<MeasurementEditViewModel>();

        builder.Services.AddTransient<SplashPage>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<RegisterPage>();
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddSingleton<CustomersPage>();
        builder.Services.AddTransient<CustomerEditPage>();
        builder.Services.AddTransient<CustomerDetailPage>();
        builder.Services.AddTransient<MeasurementEditPage>();
        builder.Services.AddTransient<OrdersPlaceholderPage>();
        builder.Services.AddTransient<PaymentsPlaceholderPage>();
        builder.Services.AddTransient<MorePlaceholderPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
