using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Domain.Entities;
using NeedleBase.Mobile.Data.Data;

namespace NeedleBase.Mobile.Tests;

/// <summary>G2: versioned local SQLite schema (PRAGMA user_version) replaces EnsureCreated.</summary>
public class LocalSchemaMigratorTests
{
    [Fact]
    public async Task FreshDatabase_MigratesToLatest_AndMatchesModel()
    {
        var migratedPath = TempDb();
        var modelPath = TempDb();

        await using (var db = Create(migratedPath))
        {
            var version = await LocalSchemaMigrator.MigrateAsync(db);
            Assert.Equal(LocalSchemaMigrator.LatestVersion, version);
        }

        await using (var db = Create(modelPath))
        {
            await db.Database.EnsureCreatedAsync();
        }

        // Fails when LocalDbContext changes without a new migration step.
        Assert.Equal(DescribeSchema(modelPath), DescribeSchema(migratedPath));
    }

    [Fact]
    public async Task LegacyEnsureCreatedDatabase_IsBaselined_WithoutDataLoss()
    {
        var path = TempDb();
        await using (var db = Create(path))
        {
            await db.Database.EnsureCreatedAsync(); // pre-G2 install
            db.Customers.Add(Customer.Create("01LEGACY0000000000000000001", "biz", "Efua Mensah", "+233245550123", null, null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        await using (var db = Create(path))
        {
            var version = await LocalSchemaMigrator.MigrateAsync(db);
            Assert.Equal(LocalSchemaMigrator.LatestVersion, version);
            Assert.Equal("Efua Mensah", (await db.Customers.SingleAsync()).Name);
        }
    }

    [Fact]
    public async Task Migrate_IsIdempotent()
    {
        var path = TempDb();
        await using var db = Create(path);
        await LocalSchemaMigrator.MigrateAsync(db);
        db.Customers.Add(Customer.Create("01KEEP00000000000000000001", "biz", "Kwame", "+233200000000", null, null, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var again = await LocalSchemaMigrator.MigrateAsync(db);

        Assert.Equal(LocalSchemaMigrator.LatestVersion, again);
        Assert.Equal(1, await db.Customers.CountAsync());
    }

    private static string TempDb() => Path.Combine(Path.GetTempPath(), $"needlebase-migr-{Guid.NewGuid():N}.db3");

    private static LocalDbContext Create(string path) =>
        new(new DbContextOptionsBuilder<LocalDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);

    private static string DescribeSchema(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        var lines = new List<string>();
        var objects = new List<(string Type, string Name)>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT type, name FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type, name;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                objects.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        foreach (var (type, name) in objects)
        {
            lines.Add($"{type}:{name}");
            if (type != "table")
            {
                continue;
            }

            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(\"{name}\");";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                // name, type, notnull, pk
                lines.Add($"  {reader.GetString(1)} {reader.GetString(2)} nn={reader.GetInt32(3)} pk={reader.GetInt32(5)}");
            }
        }

        return string.Join('\n', lines);
    }
}
