using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeedleBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Expenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    BusinessId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SpentOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OrderId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    RecordedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    IsVoided = table.Column<bool>(type: "boolean", nullable: false),
                    VoidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VoidedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SyncModifiedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    SyncTxId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Expenses_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Expenses_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BusinessId_OrderId",
                table: "Expenses",
                columns: new[] { "BusinessId", "OrderId" },
                filter: "\"OrderId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BusinessId_SpentOn",
                table: "Expenses",
                columns: new[] { "BusinessId", "SpentOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BusinessId_SyncModifiedUtc_Id",
                table: "Expenses",
                columns: new[] { "BusinessId", "SyncModifiedUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BusinessId_SyncTxId_Id",
                table: "Expenses",
                columns: new[] { "BusinessId", "SyncTxId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_OrderId",
                table: "Expenses",
                column: "OrderId");

            // Commit-ordered pull key, like the other synced tables (function from migration SyncTxOrder).
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_expenses_sync_tx BEFORE INSERT OR UPDATE ON "Expenses"
                FOR EACH ROW EXECUTE FUNCTION nb_sync_stamp_tx();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_expenses_sync_tx ON \"Expenses\";");
            migrationBuilder.DropTable(
                name: "Expenses");
        }
    }
}
