using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManualRefundClaimLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReceiptDiscrepancyId",
                table: "MANUAL_REFUND",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_REFUND_ReceiptDiscrepancyId",
                table: "MANUAL_REFUND",
                column: "ReceiptDiscrepancyId");

            migrationBuilder.AddForeignKey(
                name: "FK_MANUAL_REFUND_RECEIPT_DISCREPANCY_ReceiptDiscrepancyId",
                table: "MANUAL_REFUND",
                column: "ReceiptDiscrepancyId",
                principalTable: "RECEIPT_DISCREPANCY",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MANUAL_REFUND_RECEIPT_DISCREPANCY_ReceiptDiscrepancyId",
                table: "MANUAL_REFUND");

            migrationBuilder.DropIndex(
                name: "IX_MANUAL_REFUND_ReceiptDiscrepancyId",
                table: "MANUAL_REFUND");

            migrationBuilder.DropColumn(
                name: "ReceiptDiscrepancyId",
                table: "MANUAL_REFUND");
        }
    }
}
