namespace NeedleBase.Mobile.Data.Data;

/// <summary>The app's SQLite connection string. One place so the busy timeout can't be forgotten.</summary>
public static class LocalDbConnection
{
    /// <summary>
    /// Seconds a statement waits for a lock held by another connection (sync run vs. a form save) before
    /// failing. Microsoft.Data.Sqlite retries SQLITE_BUSY/LOCKED for this long.
    /// </summary>
    public const int BusyTimeoutSeconds = 30;

    /// <summary>
    /// Private cache (no <c>Cache=Shared</c>): shared-cache table locks make readers wait for a writer's whole
    /// transaction (SQLITE_LOCKED). With WAL (set by <see cref="LocalSchemaMigrator"/>) readers never wait for
    /// the sync writer; writers still queue behind each other for up to <see cref="BusyTimeoutSeconds"/>.
    /// </summary>
    public static string Build(string path) => $"Data Source={path};Default Timeout={BusyTimeoutSeconds}";

    /// <summary>SQLite side files of a WAL database (and a leftover rollback journal from older builds).</summary>
    public static IEnumerable<string> SideFiles(string path) => [path + "-wal", path + "-shm", path + "-journal"];
}
