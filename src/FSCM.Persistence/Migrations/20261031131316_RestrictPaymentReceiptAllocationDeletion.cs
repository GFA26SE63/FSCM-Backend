using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestrictPaymentReceiptAllocationDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PAYMENT_RECEIPT_ALLOCATION_PAYMENT_RECEIPT_PaymentReceiptId",
                table: "PAYMENT_RECEIPT_ALLOCATION");

            migrationBuilder.AddForeignKey(
                name: "FK_PAYMENT_RECEIPT_ALLOCATION_PAYMENT_RECEIPT_PaymentReceiptId",
                table: "PAYMENT_RECEIPT_ALLOCATION",
                column: "PaymentReceiptId",
                principalTable: "PAYMENT_RECEIPT",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PAYMENT_RECEIPT_ALLOCATION_PAYMENT_RECEIPT_PaymentReceiptId",
                table: "PAYMENT_RECEIPT_ALLOCATION");

            migrationBuilder.AddForeignKey(
                name: "FK_PAYMENT_RECEIPT_ALLOCATION_PAYMENT_RECEIPT_PaymentReceiptId",
                table: "PAYMENT_RECEIPT_ALLOCATION",
                column: "PaymentReceiptId",
                principalTable: "PAYMENT_RECEIPT",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
