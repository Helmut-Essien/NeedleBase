using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeedleBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Invitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    BusinessId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcceptedUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invitations_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_BusinessId_CreatedAtUtc",
                table: "Invitations",
                columns: new[] { "BusinessId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_CodeHash",
                table: "Invitations",
                column: "CodeHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Invitations");
        }
    }
}
