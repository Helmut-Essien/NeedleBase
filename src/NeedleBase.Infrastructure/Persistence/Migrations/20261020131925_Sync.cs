using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeedleBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SyncModifiedUtc",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SyncModifiedUtc",
                table: "MeasurementProfiles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SyncModifiedUtc",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.CreateTable(
                name: "DeletedItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    BusinessId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeletedItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeletedItems_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BusinessId_SyncModifiedUtc_Id",
                table: "Orders",
                columns: new[] { "BusinessId", "SyncModifiedUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementProfiles_BusinessId_SyncModifiedUtc_Id",
                table: "MeasurementProfiles",
                columns: new[] { "BusinessId", "SyncModifiedUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BusinessId_SyncModifiedUtc_Id",
                table: "Customers",
                columns: new[] { "BusinessId", "SyncModifiedUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DeletedItems_BusinessId",
                table: "DeletedItems",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_DeletedItems_BusinessId_DeletedAtUtc_Id",
                table: "DeletedItems",
                columns: new[] { "BusinessId", "DeletedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeletedItems");

            migrationBuilder.DropIndex(
                name: "IX_Orders_BusinessId_SyncModifiedUtc_Id",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_MeasurementProfiles_BusinessId_SyncModifiedUtc_Id",
                table: "MeasurementProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Customers_BusinessId_SyncModifiedUtc_Id",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SyncModifiedUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SyncModifiedUtc",
                table: "MeasurementProfiles");

            migrationBuilder.DropColumn(
                name: "SyncModifiedUtc",
                table: "Customers");
        }
    }
}
