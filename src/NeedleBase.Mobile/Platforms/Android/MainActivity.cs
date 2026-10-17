using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace NeedleBase.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Match splash dark background (#1a1a1a) — AssetTag pattern.
#pragma warning disable CA1416, CA1422
        if (Window != null)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                Window.SetDecorFitsSystemWindows(false);
            }
            else
            {
                Window.SetStatusBarColor(Android.Graphics.Color.Rgb(26, 26, 26));
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
            {
                var windowInsetsController = WindowCompat.GetInsetsController(Window, Window.DecorView);
                if (windowInsetsController != null)
                {
                    windowInsetsController.AppearanceLightStatusBars = false;
                }
            }
        }
#pragma warning restore CA1416, CA1422
    }
}
