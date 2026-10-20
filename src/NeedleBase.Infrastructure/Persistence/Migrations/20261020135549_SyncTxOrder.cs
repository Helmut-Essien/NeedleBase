using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeedleBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncTxOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SyncTxId",
                table: "Orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SyncTxId",
                table: "MeasurementProfiles",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SyncTxId",
                table: "DeletedItems",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SyncTxId",
                table: "Customers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BusinessId_SyncTxId_Id",
                table: "Orders",
                columns: new[] { "BusinessId", "SyncTxId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementProfiles_BusinessId_SyncTxId_Id",
                table: "MeasurementProfiles",
                columns: new[] { "BusinessId", "SyncTxId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DeletedItems_BusinessId_EntityType_EntityId",
                table: "DeletedItems",
                columns: new[] { "BusinessId", "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeletedItems_BusinessId_SyncTxId_Id",
                table: "DeletedItems",
                columns: new[] { "BusinessId", "SyncTxId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DeletedItems_DeletedAtUtc",
                table: "DeletedItems",
                column: "DeletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BusinessId_SyncTxId_Id",
                table: "Customers",
                columns: new[] { "BusinessId", "SyncTxId", "Id" });

            // Commit-ordered sync stamp: every insert/update records the writing transaction id.
            // Pull reads only rows with SyncTxId below the snapshot xmin, so no row can commit behind a cursor.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION nb_sync_stamp_tx() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    NEW."SyncTxId" := pg_current_xact_id()::text::bigint;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER trg_customers_sync_tx BEFORE INSERT OR UPDATE ON "Customers"
                    FOR EACH ROW EXECUTE FUNCTION nb_sync_stamp_tx();
                CREATE TRIGGER trg_measurements_sync_tx BEFORE INSERT OR UPDATE ON "MeasurementProfiles"
                    FOR EACH ROW EXECUTE FUNCTION nb_sync_stamp_tx();
                CREATE TRIGGER trg_orders_sync_tx BEFORE INSERT OR UPDATE ON "Orders"
                    FOR EACH ROW EXECUTE FUNCTION nb_sync_stamp_tx();
                CREATE TRIGGER trg_deleteditems_sync_tx BEFORE INSERT ON "DeletedItems"
                    FOR EACH ROW EXECUTE FUNCTION nb_sync_stamp_tx();
                UPDATE "Customers" SET "SyncTxId" = 0;
                UPDATE "MeasurementProfiles" SET "SyncTxId" = 0;
                UPDATE "Orders" SET "SyncTxId" = 0;
                UPDATE "DeletedItems" SET "SyncTxId" = pg_current_xact_id()::text::bigint;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_customers_sync_tx ON "Customers";
                DROP TRIGGER IF EXISTS trg_measurements_sync_tx ON "MeasurementProfiles";
                DROP TRIGGER IF EXISTS trg_orders_sync_tx ON "Orders";
                DROP TRIGGER IF EXISTS trg_deleteditems_sync_tx ON "DeletedItems";
                DROP FUNCTION IF EXISTS nb_sync_stamp_tx();
                """);


            migrationBuilder.DropIndex(
                name: "IX_Orders_BusinessId_SyncTxId_Id",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_MeasurementProfiles_BusinessId_SyncTxId_Id",
                table: "MeasurementProfiles");

            migrationBuilder.DropIndex(
                name: "IX_DeletedItems_BusinessId_EntityType_EntityId",
                table: "DeletedItems");

            migrationBuilder.DropIndex(
                name: "IX_DeletedItems_BusinessId_SyncTxId_Id",
                table: "DeletedItems");

            migrationBuilder.DropIndex(
                name: "IX_DeletedItems_DeletedAtUtc",
                table: "DeletedItems");

            migrationBuilder.DropIndex(
                name: "IX_Customers_BusinessId_SyncTxId_Id",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SyncTxId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SyncTxId",
                table: "MeasurementProfiles");

            migrationBuilder.DropColumn(
                name: "SyncTxId",
                table: "DeletedItems");

            migrationBuilder.DropColumn(
                name: "SyncTxId",
                table: "Customers");
        }
    }
}
