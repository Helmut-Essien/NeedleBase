using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NeedleBase.Application.Common;

namespace NeedleBase.Mobile.Data.Data;

/// <summary>
/// Versioned, forward-only schema upgrades for the on-device SQLite store.
/// The applied version lives in SQLite's <c>PRAGMA user_version</c>.
/// <para>
/// Rules: never edit a shipped step; append a new <see cref="Steps"/> entry for every
/// <see cref="LocalDbContext"/> model change (additive SQL that keeps existing rows).
/// <c>LocalSchemaMigratorTests</c> fails when the model and the steps drift apart.
/// </para>
/// </summary>
public static class LocalSchemaMigrator
{
    /// <summary>Version 1 = the schema that EnsureCreated produced before G2 (frozen).</summary>
    private const string V1Initial = """
        CREATE TABLE IF NOT EXISTS "Customers" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_Customers" PRIMARY KEY,
            "BusinessId" TEXT NOT NULL,
            "Name" TEXT NOT NULL,
            "Phone" TEXT NOT NULL,
            "Notes" TEXT NULL,
            "PhotoRef" TEXT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            "DateModifiedUtc" TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS "MeasurementProfiles" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_MeasurementProfiles" PRIMARY KEY,
            "BusinessId" TEXT NOT NULL,
            "CustomerId" TEXT NOT NULL,
            "GarmentTemplate" TEXT NOT NULL,
            "Unit" TEXT NOT NULL,
            "FieldsJson" TEXT NOT NULL,
            "Notes" TEXT NULL,
            "MeasuredAtUtc" TEXT NOT NULL,
            "DateModifiedUtc" TEXT NOT NULL,
            CONSTRAINT "FK_MeasurementProfiles_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS "IX_Customers_BusinessId" ON "Customers" ("BusinessId");
        CREATE INDEX IF NOT EXISTS "IX_Customers_BusinessId_Name" ON "Customers" ("BusinessId", "Name");
        CREATE INDEX IF NOT EXISTS "IX_MeasurementProfiles_BusinessId" ON "MeasurementProfiles" ("BusinessId");
        CREATE INDEX IF NOT EXISTS "IX_MeasurementProfiles_BusinessId_CustomerId_MeasuredAtUtc" ON "MeasurementProfiles" ("BusinessId", "CustomerId", "MeasuredAtUtc");
        CREATE INDEX IF NOT EXISTS "IX_MeasurementProfiles_CustomerId" ON "MeasurementProfiles" ("CustomerId");
        """;

    /// <summary>Version 2 = Orders + OrderLines (Slice 3).</summary>
    private const string V2Orders = """
        CREATE TABLE IF NOT EXISTS "Orders" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_Orders" PRIMARY KEY,
            "BusinessId" TEXT NOT NULL,
            "CustomerId" TEXT NOT NULL,
            "DisplayRef" TEXT NOT NULL,
            "Status" TEXT NOT NULL,
            "DueDate" TEXT NULL,
            "FittingDate" TEXT NULL,
            "AssignedToUserId" TEXT NULL,
            "TotalAmount" TEXT NOT NULL,
            "Notes" TEXT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            "DateModifiedUtc" TEXT NOT NULL,
            CONSTRAINT "FK_Orders_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE RESTRICT
        );
        CREATE TABLE IF NOT EXISTS "OrderLines" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_OrderLines" PRIMARY KEY,
            "BusinessId" TEXT NOT NULL,
            "OrderId" TEXT NOT NULL,
            "GarmentTemplate" TEXT NOT NULL,
            "Quantity" INTEGER NOT NULL,
            "UnitPrice" TEXT NOT NULL,
            "MeasurementProfileId" TEXT NULL,
            "Unit" TEXT NOT NULL,
            "MeasurementFieldsJson" TEXT NOT NULL,
            "Notes" TEXT NULL,
            "SortOrder" INTEGER NOT NULL,
            CONSTRAINT "FK_OrderLines_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS "IX_Orders_BusinessId" ON "Orders" ("BusinessId");
        CREATE INDEX IF NOT EXISTS "IX_Orders_BusinessId_Status" ON "Orders" ("BusinessId", "Status");
        CREATE INDEX IF NOT EXISTS "IX_Orders_BusinessId_DueDate" ON "Orders" ("BusinessId", "DueDate");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Orders_BusinessId_DisplayRef" ON "Orders" ("BusinessId", "DisplayRef");
        CREATE INDEX IF NOT EXISTS "IX_Orders_BusinessId_CustomerId" ON "Orders" ("BusinessId", "CustomerId");
        CREATE INDEX IF NOT EXISTS "IX_Orders_BusinessId_CreatedAtUtc" ON "Orders" ("BusinessId", "CreatedAtUtc");
        CREATE INDEX IF NOT EXISTS "IX_Orders_CustomerId" ON "Orders" ("CustomerId");
        CREATE INDEX IF NOT EXISTS "IX_OrderLines_BusinessId" ON "OrderLines" ("BusinessId");
        CREATE INDEX IF NOT EXISTS "IX_OrderLines_BusinessId_OrderId" ON "OrderLines" ("BusinessId", "OrderId");
        CREATE INDEX IF NOT EXISTS "IX_OrderLines_OrderId" ON "OrderLines" ("OrderId");
        """;

    /// <summary>Version 3 = customer search keys (search-as-you-type). Rows are backfilled in C#.</summary>
    private const string V3CustomerSearch = """
        ALTER TABLE "Customers" ADD COLUMN "PhoneDigits" TEXT NULL;
        ALTER TABLE "Customers" ADD COLUMN "SearchName" TEXT NULL;
        CREATE INDEX IF NOT EXISTS "IX_Customers_BusinessId_SearchName" ON "Customers" ("BusinessId", "SearchName");
        CREATE INDEX IF NOT EXISTS "IX_Customers_BusinessId_PhoneDigits" ON "Customers" ("BusinessId", "PhoneDigits");
        """;

    /// <summary>Version 4 = offline sync bookkeeping (Slice 4): queue + per-business cursor.</summary>
    private const string V4Sync = """
        CREATE TABLE IF NOT EXISTS "SyncQueue" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_SyncQueue" PRIMARY KEY,
            "BusinessId" TEXT NOT NULL,
            "EntityType" TEXT NOT NULL,
            "EntityId" TEXT NOT NULL,
            "ParentId" TEXT NULL,
            "Operation" TEXT NOT NULL,
            "PayloadJson" TEXT NULL,
            "BaseModifiedUtc" TEXT NULL,
            "Label" TEXT NOT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            "RetryCount" INTEGER NOT NULL,
            "State" TEXT NOT NULL,
            "LastError" TEXT NULL,
            "ConflictServerJson" TEXT NULL
        );
        CREATE TABLE IF NOT EXISTS "DeviceInfo" (
            "BusinessId" TEXT NOT NULL CONSTRAINT "PK_DeviceInfo" PRIMARY KEY,
            "DeviceId" TEXT NOT NULL,
            "PullCursor" TEXT NULL,
            "LastSyncUtc" TEXT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_SyncQueue_BusinessId_EntityType_EntityId" ON "SyncQueue" ("BusinessId", "EntityType", "EntityId");
        CREATE INDEX IF NOT EXISTS "IX_SyncQueue_BusinessId_State" ON "SyncQueue" ("BusinessId", "State");
        """;

    /// <summary>Version 5 = sync edge cases: conflict codes, resync flag, deferred children.</summary>
    private const string V5SyncRecovery = """
        ALTER TABLE "SyncQueue" ADD COLUMN "ConflictCode" TEXT NULL;
        ALTER TABLE "DeviceInfo" ADD COLUMN "ResyncPending" INTEGER NOT NULL DEFAULT 0;
        CREATE TABLE IF NOT EXISTS "SyncDeferred" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_SyncDeferred" PRIMARY KEY,
            "BusinessId" TEXT NOT NULL,
            "EntityType" TEXT NOT NULL,
            "EntityId" TEXT NOT NULL,
            "ParentId" TEXT NOT NULL,
            "PayloadJson" TEXT NOT NULL,
            "CreatedAtUtc" TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_SyncDeferred_BusinessId_EntityType_EntityId" ON "SyncDeferred" ("BusinessId", "EntityType", "EntityId");
        CREATE INDEX IF NOT EXISTS "IX_SyncDeferred_BusinessId_ParentId" ON "SyncDeferred" ("BusinessId", "ParentId");
        """;

    /// <summary>Ordered steps; index + 1 is the version the step produces.</summary>
    private static readonly string[] Steps =
    [
        V1Initial,
        V2Orders,
        V3CustomerSearch,
        V4Sync,
        V5SyncRecovery,
    ];

    public static int LatestVersion => Steps.Length;

    /// <summary>
    /// Brings the database to <see cref="LatestVersion"/>. Databases created by the old
    /// <c>EnsureCreated</c> (user_version 0 with tables present) are baselined as version 1
    /// without touching their rows. Returns the resulting version.
    /// </summary>
    public static async Task<int> MigrateAsync(LocalDbContext db, CancellationToken cancellationToken = default)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var version = await GetVersionAsync(connection, cancellationToken).ConfigureAwait(false);

            if (version == 0 && await TableExistsAsync(connection, "Customers", cancellationToken).ConfigureAwait(false))
            {
                // Pre-G2 install created by EnsureCreated: schema equals V1.
                await SetVersionAsync(connection, 1, cancellationToken).ConfigureAwait(false);
                version = 1;
            }

            // A newer app version's DB opened by an older build: leave it alone.
            for (var next = version + 1; next <= Steps.Length; next++)
            {
                await using var tx = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                await ExecuteAsync(connection, tx, Steps[next - 1], cancellationToken).ConfigureAwait(false);
                await ExecuteAsync(connection, tx, $"PRAGMA user_version = {next};", cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                version = next;
            }

            await BackfillCustomerSearchKeysAsync(connection, cancellationToken).ConfigureAwait(false);
            return version;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Fills SearchName/PhoneDigits for rows written before v3 (same C# rules as SaveChanges).
    /// Idempotent: only touches rows where SearchName is NULL.
    /// </summary>
    private static async Task BackfillCustomerSearchKeysAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, "Customers", cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var rows = new List<(string Id, string Name, string Phone)>();
        await using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT \"Id\", \"Name\", \"Phone\" FROM \"Customers\" WHERE \"SearchName\" IS NULL;";
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        if (rows.Count == 0)
        {
            return;
        }

        await using var tx = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (id, name, phone) in rows)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE \"Customers\" SET \"SearchName\" = $name, \"PhoneDigits\" = $phone WHERE \"Id\" = $id;";
            AddParameter(update, "$name", CustomerSearchKeys.NameKey(name));
            AddParameter(update, "$phone", CustomerSearchKeys.PhoneDigits(phone));
            AddParameter(update, "$id", id);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        command.Parameters.Add(p);
    }

    public static async Task<int> GetVersionAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Task SetVersionAsync(DbConnection connection, int version, CancellationToken cancellationToken) =>
        ExecuteAsync(connection, null, $"PRAGMA user_version = {version};", cancellationToken);

    private static async Task<bool> TableExistsAsync(DbConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$name";
        p.Value = table;
        cmd.Parameters.Add(p);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        return count > 0;
    }

    private static async Task ExecuteAsync(DbConnection connection, DbTransaction? tx, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
