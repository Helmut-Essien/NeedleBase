using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeedleBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhatsAppSentAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SentAtUtc",
                table: "WhatsAppMessages",
                type: "timestamp with time zone",
                nullable: true);

            // Already-sent rows: best known send time (delivery receipts may have moved UpdatedAtUtc later).
            migrationBuilder.Sql("UPDATE \"WhatsAppMessages\" SET \"SentAtUtc\" = \"UpdatedAtUtc\" WHERE \"ProviderMessageId\" IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_BusinessId_SentAtUtc",
                table: "WhatsAppMessages",
                columns: new[] { "BusinessId", "SentAtUtc" },
                filter: "\"SentAtUtc\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_BusinessId_SentAtUtc",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "SentAtUtc",
                table: "WhatsAppMessages");
        }
    }
}
