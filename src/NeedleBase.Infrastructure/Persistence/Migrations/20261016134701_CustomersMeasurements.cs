using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeedleBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomersMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    BusinessId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PhotoRef = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customers_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementProfiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    BusinessId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    CustomerId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    GarmentTemplate = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FieldsJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    MeasuredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DateModifiedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeasurementProfiles_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MeasurementProfiles_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BusinessId",
                table: "Customers",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BusinessId_Name",
                table: "Customers",
                columns: new[] { "BusinessId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BusinessId_Phone",
                table: "Customers",
                columns: new[] { "BusinessId", "Phone" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementProfiles_BusinessId",
                table: "MeasurementProfiles",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementProfiles_BusinessId_CustomerId_MeasuredAtUtc",
                table: "MeasurementProfiles",
                columns: new[] { "BusinessId", "CustomerId", "MeasuredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementProfiles_CustomerId",
                table: "MeasurementProfiles",
                column: "CustomerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MeasurementProfiles");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
