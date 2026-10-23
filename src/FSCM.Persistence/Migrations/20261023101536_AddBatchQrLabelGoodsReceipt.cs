using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchQrLabelGoodsReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GoodsReceiptId",
                table: "BATCH_QR_LABEL",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_QR_LABEL_GoodsReceiptId",
                table: "BATCH_QR_LABEL",
                column: "GoodsReceiptId");

            migrationBuilder.AddForeignKey(
                name: "FK_BATCH_QR_LABEL_GOODS_RECEIPT_GoodsReceiptId",
                table: "BATCH_QR_LABEL",
                column: "GoodsReceiptId",
                principalTable: "GOODS_RECEIPT",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BATCH_QR_LABEL_GOODS_RECEIPT_GoodsReceiptId",
                table: "BATCH_QR_LABEL");

            migrationBuilder.DropIndex(
                name: "IX_BATCH_QR_LABEL_GoodsReceiptId",
                table: "BATCH_QR_LABEL");

            migrationBuilder.DropColumn(
                name: "GoodsReceiptId",
                table: "BATCH_QR_LABEL");
        }
    }
}
