namespace NeedleBase.Mobile.Services;

/// <summary>Sign-in confirmations shown by the login and register screens.</summary>
public static class AuthPrompts
{
    public static (string Title, string Message, string Accept, string Cancel) OtherAccountText(int pending) =>
        ("Unsynced changes on this device",
         $"{(pending == 1 ? "1 change" : $"{pending} changes")} from the account last used here haven't synced. " +
         "Signing in with a different account deletes them from this device.",
         "Delete and sign in",
         "Cancel");

    public static Task<bool> OtherAccountAsync(int pending)
    {
        var (title, message, accept, cancel) = OtherAccountText(pending);
        return MainThread.InvokeOnMainThreadAsync(() => Shell.Current.DisplayAlert(title, message, accept, cancel));
    }
}
