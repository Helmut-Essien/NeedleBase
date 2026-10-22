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
        // Pre-G2 install: the frozen v1 tables EnsureCreated used to produce (user_version 0).
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE "Customers" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_Customers" PRIMARY KEY,
                    "BusinessId" TEXT NOT NULL, "Name" TEXT NOT NULL, "Phone" TEXT NOT NULL,
                    "Notes" TEXT NULL, "PhotoRef" TEXT NULL,
                    "CreatedAtUtc" TEXT NOT NULL, "DateModifiedUtc" TEXT NOT NULL);
                CREATE TABLE "MeasurementProfiles" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_MeasurementProfiles" PRIMARY KEY,
                    "BusinessId" TEXT NOT NULL, "CustomerId" TEXT NOT NULL, "GarmentTemplate" TEXT NOT NULL,
                    "Unit" TEXT NOT NULL, "FieldsJson" TEXT NOT NULL, "Notes" TEXT NULL,
                    "MeasuredAtUtc" TEXT NOT NULL, "DateModifiedUtc" TEXT NOT NULL);
                INSERT INTO "Customers" VALUES ('01LEGACY0000000000000000001', 'biz', 'Efua Mensah', '+233 24 555 0123', NULL, NULL, '2026-01-01 00:00:00+00:00', '2026-01-01 00:00:00+00:00');
                """;
            cmd.ExecuteNonQuery();
        }

        await using (var db = Create(path))
        {
            var version = await LocalSchemaMigrator.MigrateAsync(db);
            Assert.Equal(LocalSchemaMigrator.LatestVersion, version);
            var customer = await db.Customers.SingleAsync();
            Assert.Equal("Efua Mensah", customer.Name);

            // v3 backfill: search keys computed in C# for rows written before v3.
            var entry = db.Entry(customer);
            Assert.Equal(" efua mensah", entry.Property<string>(LocalDbContext.SearchNameColumn).CurrentValue);
            Assert.Equal("245550123", entry.Property<string>(LocalDbContext.PhoneDigitsColumn).CurrentValue);
        }
    }

    [Fact]
    public async Task V5Database_UpgradesToV6_RecomputesFoldedKeys_AndRowsStartUnknown()
    {
        var path = TempDb();
        await using (var db = Create(path))
        {
            await LocalSchemaMigrator.MigrateAsync(db);
        }

        // Turn the fresh database back into a v5 one holding a row with a pre-folding search key.
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                DROP INDEX "IX_Customers_Unknown"; DROP INDEX "IX_MeasurementProfiles_Unknown"; DROP INDEX "IX_Orders_Unknown";
                ALTER TABLE "Customers" DROP COLUMN "ServerKnown";
                ALTER TABLE "MeasurementProfiles" DROP COLUMN "ServerKnown";
                ALTER TABLE "Orders" DROP COLUMN "ServerKnown";
                ALTER TABLE "DeviceInfo" DROP COLUMN "ResyncRecover";
                INSERT INTO "Customers" ("Id","BusinessId","Name","Phone","Notes","PhotoRef","CreatedAtUtc","DateModifiedUtc","SearchName","PhoneDigits")
                VALUES ('01V5ROW0000000000000000001','biz','Ámá Owusu-Ansah','+233245550123',NULL,NULL,'2026-01-01 00:00:00+00:00','2026-01-01 00:00:00+00:00',' ámá owusu-ansah','245550123');
                PRAGMA user_version = 5;
                """;
            cmd.ExecuteNonQuery();
        }

        await using (var db = Create(path))
        {
            Assert.Equal(LocalSchemaMigrator.LatestVersion, await LocalSchemaMigrator.MigrateAsync(db));
            var customer = await db.Customers.SingleAsync();
            var entry = db.Entry(customer);
            Assert.Equal(" ama owusu ansah", entry.Property<string>(LocalDbContext.SearchNameColumn).CurrentValue);
            Assert.False(entry.Property<bool>(LocalDbContext.ServerKnownColumn).CurrentValue);
        }

        Assert.Equal(Normalize(DescribeSchema(FreshPath())), Normalize(DescribeSchema(path)));
    }

    [Fact]
    public async Task V7DatabaseWithRows_UpgradesToV8_AddsPayments_KeepsData()
    {
        var path = TempDb();
        await using (var db = Create(path))
        {
            await LocalSchemaMigrator.MigrateAsync(db);
        }

        // A v7 install (before Slice 5) that already holds a customer.
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                DROP TABLE "Payments";
                INSERT INTO "Customers" ("Id","BusinessId","Name","Phone","Notes","PhotoRef","CreatedAtUtc","DateModifiedUtc","SearchName","PhoneDigits")
                VALUES ('01V7ROW0000000000000000001','biz','Efua Mensah','+233245550123',NULL,NULL,'2026-01-01 00:00:00+00:00','2026-01-01 00:00:00+00:00',' efua mensah','245550123');
                PRAGMA user_version = 7;
                """;
            cmd.ExecuteNonQuery();
        }

        await using (var db = Create(path))
        {
            Assert.Equal(8, await LocalSchemaMigrator.MigrateAsync(db));
            Assert.Equal("Efua Mensah", (await db.Customers.SingleAsync()).Name);
            Assert.Equal(0, await db.Payments.CountAsync());
        }

        Assert.Equal(Normalize(DescribeSchema(FreshPath())), Normalize(DescribeSchema(path)));
    }

    [Fact]
    public async Task KeyRecompute_LargeBook_IsQuick()
    {
        const int rows = 20_000;
        var path = TempDb();
        await using (var db = Create(path))
        {
            await LocalSchemaMigrator.MigrateAsync(db);
        }

        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var tx = connection.BeginTransaction();
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO "Customers" ("Id","BusinessId","Name","Phone","Notes","PhotoRef","CreatedAtUtc","DateModifiedUtc","SearchName","PhoneDigits","ServerKnown")
                VALUES ($id,'biz',$name,'+233245550123',NULL,NULL,'2026-01-01 00:00:00+00:00','2026-01-01 00:00:00+00:00',NULL,NULL,1);
                """;
            var id = cmd.Parameters.Add("$id", SqliteType.Text);
            var name = cmd.Parameters.Add("$name", SqliteType.Text);
            for (var i = 0; i < rows; i++)
            {
                id.Value = $"01BULK{i:D20}";
                name.Value = $"Ámá Owusu-Ansah {i}";
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await using (var db = Create(path))
        {
            await LocalSchemaMigrator.MigrateAsync(db);   // backfills every NULL key
            Assert.Equal(0, await db.Customers.CountAsync(c => EF.Property<string>(c, LocalDbContext.SearchNameColumn) == null));
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"backfill took {watch.Elapsed}");
    }

    private static string FreshPath()
    {
        var path = TempDb();
        using var db = Create(path);
        LocalSchemaMigrator.MigrateAsync(db).GetAwaiter().GetResult();
        return path;
    }

    // Column order differs after DROP/ADD COLUMN; compare as sets.
    private static string Normalize(string schema) => string.Join('\n', schema.Split('\n').Select(l => l.Trim()).Order(StringComparer.Ordinal));

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
