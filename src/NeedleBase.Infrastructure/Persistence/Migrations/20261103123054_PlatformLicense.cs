using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeedleBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlatformLicense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LicenseAttachedAtUtc",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LicenseCheckedAtUtc",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LicenseExpiresAtUtc",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LicenseFailedChecks",
                table: "Businesses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LicenseGraceUntilUtc",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseId",
                table: "Businesses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LicenseInvalidSinceUtc",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LicenseIsComplimentary",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LicenseIsTrial",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LicenseIsValid",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LicenseKeyHint",
                table: "Businesses",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseKeyProtected",
                table: "Businesses",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LicenseNextCheckUtc",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicensePlan",
                table: "Businesses",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseReason",
                table: "Businesses",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LicenseRefreshAfterUtc",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseToken",
                table: "Businesses",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_LicenseNextCheckUtc",
                table: "Businesses",
                column: "LicenseNextCheckUtc",
                filter: "\"LicenseKeyProtected\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Businesses_LicenseNextCheckUtc",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseAttachedAtUtc",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseCheckedAtUtc",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseExpiresAtUtc",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseFailedChecks",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseGraceUntilUtc",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseId",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseInvalidSinceUtc",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseIsComplimentary",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseIsTrial",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseIsValid",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseKeyHint",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseKeyProtected",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseNextCheckUtc",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicensePlan",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseReason",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseRefreshAfterUtc",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseToken",
                table: "Businesses");
        }
    }
}
