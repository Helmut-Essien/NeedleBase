namespace NeedleBase.Mobile.Data.Services;

public interface ILocalDatabaseService
{
    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Wipes all local SQLite data. Required on explicit logout so the next user
    /// cannot read prior tenant PII. Returns false if wipe fails.
    /// </summary>
    Task<bool> WipeAsync(CancellationToken cancellationToken = default);
}
