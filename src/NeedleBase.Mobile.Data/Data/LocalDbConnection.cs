namespace NeedleBase.Mobile.Data.Data;

/// <summary>The app's SQLite connection string. One place so the busy timeout can't be forgotten.</summary>
public static class LocalDbConnection
{
    /// <summary>
    /// Seconds a statement waits for a lock held by another connection (sync run vs. a form save) before
    /// failing. Microsoft.Data.Sqlite retries SQLITE_BUSY/LOCKED for this long.
    /// </summary>
    public const int BusyTimeoutSeconds = 30;

    public static string Build(string path) => $"Data Source={path};Cache=Shared;Default Timeout={BusyTimeoutSeconds}";
}
