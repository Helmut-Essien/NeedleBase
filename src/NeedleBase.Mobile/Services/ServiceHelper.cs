namespace NeedleBase.Mobile.Services;

internal static class ServiceHelper
{
    public static T GetRequiredService<T>() where T : notnull
    {
        var services = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("MAUI services are not available yet.");
        return services.GetRequiredService<T>();
    }

    /// <summary>Null while MAUI services aren't available yet (e.g. very early window events).</summary>
    public static T? GetService<T>() where T : class =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetService<T>();
}
