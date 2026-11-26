using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeedleBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaidPlanEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DueEmailSentForDate",
                table: "Orders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MonthlySummarySentFor",
                table: "Businesses",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "WeeklySummarySentFor",
                table: "Businesses",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_DueDate_DueEmailSentForDate",
                table: "Orders",
                columns: new[] { "DueDate", "DueEmailSentForDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_MonthlySummarySentFor",
                table: "Businesses",
                column: "MonthlySummarySentFor");

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_WeeklySummarySentFor",
                table: "Businesses",
                column: "WeeklySummarySentFor");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_DueDate_DueEmailSentForDate",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Businesses_MonthlySummarySentFor",
                table: "Businesses");

            migrationBuilder.DropIndex(
                name: "IX_Businesses_WeeklySummarySentFor",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "DueEmailSentForDate",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "MonthlySummarySentFor",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "WeeklySummarySentFor",
                table: "Businesses");
        }
    }
}
