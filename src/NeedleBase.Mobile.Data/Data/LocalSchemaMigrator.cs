using System.Data.Common;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>Ordered steps; index + 1 is the version the step produces.</summary>
    private static readonly string[] Steps =
    [
        V1Initial,
        V2Orders,
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
