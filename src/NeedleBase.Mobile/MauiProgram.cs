using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using NeedleBase.Mobile.Configuration;
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

        builder.Services.AddHttpClient("AuthClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        builder.Services.AddSingleton<ISecureStorageService, MauiSecureStorageService>();
        builder.Services.AddSingleton<IAuthService, AuthService>();
        builder.Services.AddSingleton<INavigationService, NavigationService>();

        builder.Services.AddTransient<SplashViewModel>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<RegisterViewModel>();
        builder.Services.AddSingleton<HomeViewModel>();

        builder.Services.AddTransient<SplashPage>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<RegisterPage>();
        builder.Services.AddSingleton<HomePage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
