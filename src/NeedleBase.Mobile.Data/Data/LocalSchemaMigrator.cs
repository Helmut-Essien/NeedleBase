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

    /// <summary>
    /// Version 6 = second audit: server-known flag (adoption of rows from pre-sync builds; existing rows start
    /// unknown and are confirmed by one full resync) and accent-folded search keys (reset here, recomputed by
    /// the backfill below, which only touches NULL keys and so is safe to rerun).
    /// </summary>
    private const string V6Adoption = """
        ALTER TABLE "Customers" ADD COLUMN "ServerKnown" INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE "MeasurementProfiles" ADD COLUMN "ServerKnown" INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE "Orders" ADD COLUMN "ServerKnown" INTEGER NOT NULL DEFAULT 0;
        CREATE INDEX IF NOT EXISTS "IX_Customers_Unknown" ON "Customers" ("BusinessId") WHERE "ServerKnown" = 0;
        CREATE INDEX IF NOT EXISTS "IX_MeasurementProfiles_Unknown" ON "MeasurementProfiles" ("BusinessId") WHERE "ServerKnown" = 0;
        CREATE INDEX IF NOT EXISTS "IX_Orders_Unknown" ON "Orders" ("BusinessId") WHERE "ServerKnown" = 0;
        ALTER TABLE "DeviceInfo" ADD COLUMN "ResyncRecover" INTEGER NOT NULL DEFAULT 0;
        UPDATE "Customers" SET "SearchName" = NULL;
        """;

    /// <summary>
    /// Version 7 = phone normalisation (+233 form, <see cref="NeedleBase.Domain.Common.PhoneNumbers"/>): keys are
    /// reset and the backfill below rewrites Phone with the keys. Not queued for sync: the server runs the same
    /// rule on its own rows, so both sides converge without a push.
    /// </summary>
    private const string V7PhoneNormalize = """
        UPDATE "Customers" SET "SearchName" = NULL;
        """;

    /// <summary>
    /// Version 8 = Payments (Slice 5). Cascade from Orders: the server refuses to delete an order with payments
    /// and the phone refuses locally, so the cascade only runs when a pulled delete has nothing to keep.
    /// </summary>
    private const string V8Payments = """
        CREATE TABLE IF NOT EXISTS "Payments" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_Payments" PRIMARY KEY,
            "BusinessId" TEXT NOT NULL,
            "OrderId" TEXT NOT NULL,
            "CustomerId" TEXT NOT NULL,
            "Amount" TEXT NOT NULL,
            "Method" TEXT NOT NULL,
            "Note" TEXT NULL,
            "RecordedAtUtc" TEXT NOT NULL,
            "IsVoided" INTEGER NOT NULL,
            "VoidedAtUtc" TEXT NULL,
            "VoidReason" TEXT NULL,
            "CreatedAtUtc" TEXT NOT NULL,
            "DateModifiedUtc" TEXT NOT NULL,
            "ServerKnown" INTEGER NOT NULL DEFAULT 0,
            CONSTRAINT "FK_Payments_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS "IX_Payments_BusinessId" ON "Payments" ("BusinessId");
        CREATE INDEX IF NOT EXISTS "IX_Payments_BusinessId_OrderId" ON "Payments" ("BusinessId", "OrderId");
        CREATE INDEX IF NOT EXISTS "IX_Payments_BusinessId_RecordedAtUtc" ON "Payments" ("BusinessId", "RecordedAtUtc");
        CREATE INDEX IF NOT EXISTS "IX_Payments_OrderId" ON "Payments" ("OrderId");
        CREATE INDEX IF NOT EXISTS "IX_Payments_Unknown" ON "Payments" ("BusinessId") WHERE "ServerKnown" = 0;
        """;

    /// <summary>
    /// Version 9 = order editing: when each line's measurement snapshot was taken. Existing lines stay NULL
    /// ("date not recorded"); no backfill, because the profile may have changed since the order was made.
    /// </summary>
    private const string V9MeasurementTakenAt = """
        ALTER TABLE "OrderLines" ADD COLUMN "MeasurementTakenAtUtc" TEXT NULL;
        """;

    /// <summary>Ordered steps; index + 1 is the version the step produces.</summary>
    private static readonly string[] Steps =
    [
        V1Initial,
        V2Orders,
        V3CustomerSearch,
        V4Sync,
        V5SyncRecovery,
        V6Adoption,
        V7PhoneNormalize,
        V8Payments,
        V9MeasurementTakenAt,
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
            // WAL: list reads don't wait behind sync writes. Persistent in the file; must run outside a
            // transaction; a no-op ("memory") for in-memory databases.
            await ExecuteAsync(connection, null, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
            // Start each launch with an empty -wal so it can't keep growing on small phones (autocheckpoint
            // stays at SQLite's default 1000 pages; this only reclaims the file). No-op without WAL.
            await ExecuteAsync(connection, null, "PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken).ConfigureAwait(false);
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
                // ADD COLUMN has no IF NOT EXISTS in SQLite: skip V9 when the column is already there
                // (a half-applied or rebuilt database) so the step stays idempotent.
                var skip = ReferenceEquals(Steps[next - 1], V9MeasurementTakenAt)
                    && await ColumnExistsAsync(connection, tx, "OrderLines", "MeasurementTakenAtUtc", cancellationToken).ConfigureAwait(false);
                if (!skip)
                {
                    await ExecuteAsync(connection, tx, Steps[next - 1], cancellationToken).ConfigureAwait(false);
                }

                await ExecuteAsync(connection, tx, $"PRAGMA user_version = {next};", cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                version = next;
            }

            await BackfillCustomerSearchKeysAsync(connection, cancellationToken).ConfigureAwait(false);
            return version;
        }
        finally
        {
            // Raw ADO writes (steps, v3/v7 backfill) bypass the EF interceptors: invalidate cached lists.
            LocalDataVersion.Bump();
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Fills SearchName/PhoneDigits for rows written before v3 (same C# rules as SaveChanges) and, since v7,
    /// normalises Phone (same rule as the domain).
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

        // One transaction and one prepared command: large books (tens of thousands of customers) take well under a second.
        await using var tx = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var update = connection.CreateCommand();
        update.Transaction = tx;
        update.CommandText = "UPDATE \"Customers\" SET \"SearchName\" = $name, \"PhoneDigits\" = $phone, \"Phone\" = $number WHERE \"Id\" = $id;";
        AddParameter(update, "$name", string.Empty);
        AddParameter(update, "$phone", string.Empty);
        AddParameter(update, "$number", string.Empty);
        AddParameter(update, "$id", string.Empty);
        update.Prepare();
        foreach (var (id, name, phone) in rows)
        {
            var number = NeedleBase.Domain.Common.PhoneNumbers.Normalize(phone);
            if (number.Length == 0 || number.Length > NeedleBase.Domain.Common.FieldLimits.Phone)
            {
                number = phone;
            }

            update.Parameters["$name"].Value = CustomerSearchKeys.NameKey(name);
            update.Parameters["$phone"].Value = CustomerSearchKeys.PhoneDigits(number);
            update.Parameters["$number"].Value = number;
            update.Parameters["$id"].Value = id;
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

    private static async Task<bool> ColumnExistsAsync(
        DbConnection connection, DbTransaction tx, string table, string column, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info($table) WHERE name = $column;";
        AddParameter(command, "$table", table);
        AddParameter(command, "$column", column);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

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
