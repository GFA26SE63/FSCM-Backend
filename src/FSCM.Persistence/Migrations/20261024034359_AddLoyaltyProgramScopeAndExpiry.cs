using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoyaltyProgramScopeAndExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PointsExpireAt",
                table: "LOYALTY_PROGRAM",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LOYALTY_PROGRAM_RETAILER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoyaltyProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetailerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LOYALTY_PROGRAM_RETAILER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LOYALTY_PROGRAM_RETAILER_LOYALTY_PROGRAM_LoyaltyProgramId",
                        column: x => x.LoyaltyProgramId,
                        principalTable: "LOYALTY_PROGRAM",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LOYALTY_PROGRAM_RETAILER_RETAILER_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "RETAILER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LOYALTY_PROGRAM_RETAILER_LoyaltyProgramId_RetailerId",
                table: "LOYALTY_PROGRAM_RETAILER",
                columns: new[] { "LoyaltyProgramId", "RetailerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LOYALTY_PROGRAM_RETAILER_RetailerId",
                table: "LOYALTY_PROGRAM_RETAILER",
                column: "RetailerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LOYALTY_PROGRAM_RETAILER");

            migrationBuilder.DropColumn(
                name: "PointsExpireAt",
                table: "LOYALTY_PROGRAM");
        }
    }
}
