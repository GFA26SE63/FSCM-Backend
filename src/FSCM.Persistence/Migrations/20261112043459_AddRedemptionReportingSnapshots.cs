using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRedemptionReportingSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "REWARD_REDEMPTION",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                table: "REWARD_REDEMPTION",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveredAt",
                table: "REWARD_REDEMPTION",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveredByUserId",
                table: "REWARD_REDEMPTION",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "REWARD_REDEMPTION",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequiredPointsSnapshot",
                table: "REWARD_REDEMPTION",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "REWARD_REDEMPTION");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "REWARD_REDEMPTION");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "REWARD_REDEMPTION");

            migrationBuilder.DropColumn(
                name: "DeliveredByUserId",
                table: "REWARD_REDEMPTION");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "REWARD_REDEMPTION");

            migrationBuilder.DropColumn(
                name: "RequiredPointsSnapshot",
                table: "REWARD_REDEMPTION");
        }
    }
}
