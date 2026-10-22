using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NeedleBase.Mobile.Data.Data;

/// <summary>
/// Process-wide counter bumped on every local write (form saves, sync push/pull applies, bulk deletes, raw SQL,
/// wipe). Lists cache their rows and reload only when this changed (or the business/day changed).
/// </summary>
public static class LocalDataVersion
{
    private static long _version;

    public static long Current => Interlocked.Read(ref _version);

    public static void Bump() => Interlocked.Increment(ref _version);

    /// <summary>
    /// Raised after a background sync committed a pull page that changed rows. Fires on the sync thread;
    /// subscribers (visible lists via LiveListRefresh) must marshal to the UI themselves.
    /// </summary>
    public static event Action<string>? ChangedBySync;

    /// <param name="businessId">Workspace whose rows changed.</param>
    public static void NotifyChangedBySync(string businessId) => ChangedBySync?.Invoke(businessId);
}

/// <summary>Bumps <see cref="LocalDataVersion"/> for non-query commands (ExecuteDelete/Update, raw SQL).</summary>
public sealed class LocalDataVersionInterceptor : DbCommandInterceptor
{
    public static readonly LocalDataVersionInterceptor Instance = new();

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        LocalDataVersion.Bump();
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        LocalDataVersion.Bump();
        return ValueTask.FromResult(result);
    }
}

/// <summary>
/// Bumps <see cref="LocalDataVersion"/> again when an explicit transaction commits or rolls back. Writes inside a
/// transaction bump before commit; under WAL a list read in that window still sees the old rows and would
/// otherwise cache them against the new version.
/// </summary>
public sealed class LocalDataVersionTransactionInterceptor : DbTransactionInterceptor
{
    public static readonly LocalDataVersionTransactionInterceptor Instance = new();

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        LocalDataVersion.Bump();

    public override Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        LocalDataVersion.Bump();
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        LocalDataVersion.Bump();

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        LocalDataVersion.Bump();
        return Task.CompletedTask;
    }
}
