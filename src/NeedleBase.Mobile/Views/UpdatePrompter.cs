using NeedleBase.Mobile.Services.Updates;

namespace NeedleBase.Mobile.Views;

/// <summary>Shows <see cref="UpdatePage"/> at most once at a time; runs the automatic check off the start-up path.</summary>
internal static class UpdatePrompter
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private static int _checking;

    /// <summary>On every return to the foreground: throttled check (or cached required update), never throws.</summary>
    public static void CheckInBackground(AppUpdateService? updates)
    {
        if (updates is not { IsEnabled: true } || Interlocked.Exchange(ref _checking, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(StartupDelay); // let login, first sync and the first screen go first
                var result = await updates.CheckAsync(manual: false);
                if (result.Kind != UpdateKind.None && result.Manifest is not null)
                {
                    await MainThread.InvokeOnMainThreadAsync(() => ShowAsync(updates, result));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Updates: background check failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
            }
        });
    }

    public static async Task ShowAsync(AppUpdateService updates, UpdateCheckResult result)
    {
        var navigation = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
        if (navigation is null || result.Manifest is null || navigation.ModalStack.OfType<UpdatePage>().Any())
        {
            return;
        }

        await navigation.PushModalAsync(new UpdatePage(updates, result));
    }
}
