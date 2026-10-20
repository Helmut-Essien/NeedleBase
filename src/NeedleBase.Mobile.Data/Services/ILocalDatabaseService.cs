namespace NeedleBase.Mobile.Data.Services;

public interface ILocalDatabaseService
{
    /// <summary>
    /// Creates or upgrades the local SQLite schema to the latest version without data loss
    /// (see <see cref="Data.LocalSchemaMigrator"/>).
    /// </summary>
    Task MigrateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Wipes all local SQLite data. Required on explicit logout so the next user
    /// cannot read prior tenant PII. Returns false if wipe fails.
    /// </summary>
    Task<bool> WipeAsync(CancellationToken cancellationToken = default);

    /// <summary>Changes on this device that haven't reached the server (all businesses). Used before a wipe.</summary>
    Task<int> CountUnsyncedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}
