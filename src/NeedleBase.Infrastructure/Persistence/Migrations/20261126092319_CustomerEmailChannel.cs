using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NeedleBase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerEmailChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerEmailConsents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    BusinessId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    CustomerId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    OptedIn = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ChangedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerEmailConsents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerEmailConsents_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerEmailConsents_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerEmailSents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    BusinessId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RefId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    StatusKey = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerEmailSents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerEmailSents_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerEmailConsents_BusinessId",
                table: "CustomerEmailConsents",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerEmailConsents_CustomerId",
                table: "CustomerEmailConsents",
                column: "CustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerEmailSents_BusinessId",
                table: "CustomerEmailSents",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerEmailSents_Kind_RefId_StatusKey",
                table: "CustomerEmailSents",
                columns: new[] { "Kind", "RefId", "StatusKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerEmailConsents");

            migrationBuilder.DropTable(
                name: "CustomerEmailSents");
        }
    }
}
