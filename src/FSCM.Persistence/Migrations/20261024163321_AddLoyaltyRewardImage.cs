using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLoyaltyRewardImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LOYALTY_PROGRAM_RETAILER_LOYALTY_PROGRAM_LoyaltyProgramId",
                table: "LOYALTY_PROGRAM_RETAILER");

            migrationBuilder.DropForeignKey(
                name: "FK_LOYALTY_PROGRAM_RETAILER_RETAILER_RetailerId",
                table: "LOYALTY_PROGRAM_RETAILER");

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "LOYALTY_REWARD",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LOYALTY_PROGRAM_RETAILER_LOYALTY_PROGRAM_LoyaltyProgramId",
                table: "LOYALTY_PROGRAM_RETAILER",
                column: "LoyaltyProgramId",
                principalTable: "LOYALTY_PROGRAM",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LOYALTY_PROGRAM_RETAILER_RETAILER_RetailerId",
                table: "LOYALTY_PROGRAM_RETAILER",
                column: "RetailerId",
                principalTable: "RETAILER",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LOYALTY_PROGRAM_RETAILER_LOYALTY_PROGRAM_LoyaltyProgramId",
                table: "LOYALTY_PROGRAM_RETAILER");

            migrationBuilder.DropForeignKey(
                name: "FK_LOYALTY_PROGRAM_RETAILER_RETAILER_RetailerId",
                table: "LOYALTY_PROGRAM_RETAILER");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "LOYALTY_REWARD");

            migrationBuilder.AddForeignKey(
                name: "FK_LOYALTY_PROGRAM_RETAILER_LOYALTY_PROGRAM_LoyaltyProgramId",
                table: "LOYALTY_PROGRAM_RETAILER",
                column: "LoyaltyProgramId",
                principalTable: "LOYALTY_PROGRAM",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LOYALTY_PROGRAM_RETAILER_RETAILER_RetailerId",
                table: "LOYALTY_PROGRAM_RETAILER",
                column: "RetailerId",
                principalTable: "RETAILER",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
