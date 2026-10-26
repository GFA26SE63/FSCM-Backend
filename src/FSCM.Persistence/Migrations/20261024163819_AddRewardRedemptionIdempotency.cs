using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRewardRedemptionIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_REWARD_REDEMPTION_RetailerId",
                table: "REWARD_REDEMPTION");

            migrationBuilder.AddColumn<string>(
                name: "ClientRequestId",
                table: "REWARD_REDEMPTION",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_REWARD_REDEMPTION_RetailerId_ClientRequestId",
                table: "REWARD_REDEMPTION",
                columns: new[] { "RetailerId", "ClientRequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_REWARD_REDEMPTION_RetailerId_ClientRequestId",
                table: "REWARD_REDEMPTION");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "REWARD_REDEMPTION");

            migrationBuilder.CreateIndex(
                name: "IX_REWARD_REDEMPTION_RetailerId",
                table: "REWARD_REDEMPTION",
                column: "RetailerId");
        }
    }
}
