using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeedleBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderLineMeasurementTakenAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MeasurementTakenAtUtc",
                table: "OrderLines",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MeasurementTakenAtUtc",
                table: "OrderLines");
        }
    }
}
