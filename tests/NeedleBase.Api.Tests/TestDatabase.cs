using Microsoft.EntityFrameworkCore;
using NeedleBase.Infrastructure.Persistence;
using Npgsql;

namespace NeedleBase.Api.Tests;

/// <summary>
/// Brings the test database to the latest migration exactly once per test run, before any test host starts.
/// Without this every factory's host migrated an empty database at the same time (CI: a fresh PostgreSQL service)
/// and requests arrived before the schema existed. A PostgreSQL advisory lock also serialises separate processes
/// (e.g. two test runs against the same database).
/// </summary>
internal static class TestDatabase
{
    public const string DefaultConnection = "Host=localhost;Port=5435;Database=NeedleBase;Username=needlebase;Password=needlebase_dev";
    private const long MigrationLockKey = 0x4E65656442617365; // "NeedBase"

    private static readonly Lazy<Task> Migrated = new(() => MigrateAsync(ConnectionString), LazyThreadSafetyMode.ExecutionAndPublication);

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? DefaultConnection;

    /// <summary>Blocks until migrated. A failure is cached, so every test reports the same clear error.</summary>
    public static void EnsureMigrated() => Migrated.Value.GetAwaiter().GetResult();

    private static async Task MigrateAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using (var take = new NpgsqlCommand($"SELECT pg_advisory_lock({MigrationLockKey})", connection))
        {
            await take.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options;
            await using var db = new ApplicationDbContext(options);
            await db.Database.MigrateAsync().ConfigureAwait(false);
        }
        finally
        {
            await using var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({MigrationLockKey})", connection);
            await release.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
