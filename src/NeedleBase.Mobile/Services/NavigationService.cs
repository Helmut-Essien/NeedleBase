namespace NeedleBase.Mobile.Services;

public interface INavigationService
{
    Task ShowLoginAsync();
    Task ShowMainAsync();
}

public sealed class NavigationService : INavigationService
{
    public async Task ShowLoginAsync()
    {
        if (Shell.Current is null)
        {
            return;
        }

        // Hide app shell content and go to login (do not wipe SQLite on session expiry).
        await Shell.Current.GoToAsync("//LoginPage");
    }

    public async Task ShowMainAsync()
    {
        if (Shell.Current is null)
        {
            return;
        }

        await Shell.Current.GoToAsync("//HomePage");
    }
}
