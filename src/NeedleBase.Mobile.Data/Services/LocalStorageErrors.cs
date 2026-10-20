using Microsoft.Data.Sqlite;

namespace NeedleBase.Mobile.Data.Services;

/// <summary>Recognises local storage failures so the user sees a plain message instead of a crash or a generic error.</summary>
public static class LocalStorageErrors
{
    public const string StorageFullMessage =
        "Your phone's storage is full, so this couldn't be saved. Free up some space, then try again.";

    private const int SqliteFull = 13;      // SQLITE_FULL
    private const int SqliteIoErr = 10;     // SQLITE_IOERR (extended codes carry ENOSPC as IOERR_WRITE)
    private const int EnospcHResult = 28;   // Linux/Android ENOSPC
    private const int DiskFullHResult = unchecked((int)0x80070070); // Windows ERROR_DISK_FULL

    public static bool IsStorageFull(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case SqliteException { SqliteErrorCode: SqliteFull }:
                    return true;
                case SqliteException sqlite when sqlite.SqliteErrorCode == SqliteIoErr &&
                                                 sqlite.Message.Contains("full", StringComparison.OrdinalIgnoreCase):
                    return true;
                case IOException io when io.HResult is EnospcHResult or DiskFullHResult:
                    return true;
            }
        }

        return false;
    }

    /// <summary>The storage-full message when it applies, otherwise null (callers keep their own copy).</summary>
    public static string? Describe(Exception? ex) => IsStorageFull(ex) ? StorageFullMessage : null;
}
