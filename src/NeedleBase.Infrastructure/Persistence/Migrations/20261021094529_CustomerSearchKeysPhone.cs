using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeedleBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerSearchKeysPhone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneDigits",
                table: "Customers",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchName",
                table: "Customers",
                type: "character varying(201)",
                maxLength: 201,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BusinessId_PhoneDigits",
                table: "Customers",
                columns: new[] { "BusinessId", "PhoneDigits" })
                .Annotation("Npgsql:IndexOperators", new[] { null, "varchar_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BusinessId_SearchName",
                table: "Customers",
                columns: new[] { "BusinessId", "SearchName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_BusinessId_PhoneDigits",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_BusinessId_SearchName",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PhoneDigits",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SearchName",
                table: "Customers");
        }
    }
}
