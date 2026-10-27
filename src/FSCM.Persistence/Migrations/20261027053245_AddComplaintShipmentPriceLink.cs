using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddComplaintShipmentPriceLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdjustmentAmount",
                table: "RECEIPT_DISCREPANCY_ITEM",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "AllocationPlanPriceId",
                table: "RECEIPT_DISCREPANCY_ITEM",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalUnitPrice",
                table: "RECEIPT_DISCREPANCY_ITEM",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentId",
                table: "RECEIPT_DISCREPANCY_ITEM",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdjustmentAmount",
                table: "RECEIPT_DISCREPANCY_ITEM");

            migrationBuilder.DropColumn(
                name: "AllocationPlanPriceId",
                table: "RECEIPT_DISCREPANCY_ITEM");

            migrationBuilder.DropColumn(
                name: "FinalUnitPrice",
                table: "RECEIPT_DISCREPANCY_ITEM");

            migrationBuilder.DropColumn(
                name: "ShipmentId",
                table: "RECEIPT_DISCREPANCY_ITEM");
        }
    }
}
