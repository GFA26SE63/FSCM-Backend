using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignReview2OrderPaymentPickingAndComplaints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SALES_ORDER_PaymentMethod",
                table: "SALES_ORDER");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SALES_ORDER_Status",
                table: "SALES_ORDER");

            migrationBuilder.AlterColumn<short>(
                name: "PaymentMethod",
                table: "SALES_ORDER",
                type: "smallint",
                nullable: true,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AddColumn<Guid>(
                name: "AllocationId",
                table: "RECEIPT_DISCREPANCY_ITEM",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SALES_ORDER_PaymentMethod",
                table: "SALES_ORDER",
                sql: "\"PaymentMethod\" IS NULL OR \"PaymentMethod\" BETWEEN 0 AND 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SALES_ORDER_Status",
                table: "SALES_ORDER",
                sql: "\"Status\" BETWEEN 0 AND 14");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_DISCREPANCY_ITEM_AllocationId",
                table: "RECEIPT_DISCREPANCY_ITEM",
                column: "AllocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_RECEIPT_DISCREPANCY_ITEM_ALLOCATION_AllocationId",
                table: "RECEIPT_DISCREPANCY_ITEM",
                column: "AllocationId",
                principalTable: "ALLOCATION",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RECEIPT_DISCREPANCY_ITEM_ALLOCATION_AllocationId",
                table: "RECEIPT_DISCREPANCY_ITEM");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SALES_ORDER_PaymentMethod",
                table: "SALES_ORDER");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SALES_ORDER_Status",
                table: "SALES_ORDER");

            migrationBuilder.DropIndex(
                name: "IX_RECEIPT_DISCREPANCY_ITEM_AllocationId",
                table: "RECEIPT_DISCREPANCY_ITEM");

            migrationBuilder.DropColumn(
                name: "AllocationId",
                table: "RECEIPT_DISCREPANCY_ITEM");

            migrationBuilder.AlterColumn<short>(
                name: "PaymentMethod",
                table: "SALES_ORDER",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SALES_ORDER_PaymentMethod",
                table: "SALES_ORDER",
                sql: "\"PaymentMethod\" BETWEEN 0 AND 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SALES_ORDER_Status",
                table: "SALES_ORDER",
                sql: "\"Status\" BETWEEN 0 AND 9");
        }
    }
}
