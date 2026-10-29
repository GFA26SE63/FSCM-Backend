using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentReceiptActuals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RECEIPT_CONFIRMATION_ITEM_ReceiptConfirmationId",
                table: "RECEIPT_CONFIRMATION_ITEM");

            migrationBuilder.DropIndex(
                name: "IX_RECEIPT_CONFIRMATION_SalesOrderId",
                table: "RECEIPT_CONFIRMATION");

            migrationBuilder.AddColumn<decimal>(
                name: "DamagedQuantity",
                table: "RECEIPT_CONFIRMATION_ITEM",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MissingQuantity",
                table: "RECEIPT_CONFIRMATION_ITEM",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesOrderLineId",
                table: "RECEIPT_CONFIRMATION_ITEM",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentId",
                table: "RECEIPT_CONFIRMATION",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RECEIPT_CONFIRMATION_QR_LABEL",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptConfirmationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchQrLabelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RECEIPT_CONFIRMATION_QR_LABEL", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RECEIPT_CONFIRMATION_QR_LABEL_BATCH_QR_LABEL_BatchQrLabelId",
                        column: x => x.BatchQrLabelId,
                        principalTable: "BATCH_QR_LABEL",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RECEIPT_CONFIRMATION_QR_LABEL_RECEIPT_CONFIRMATION_ReceiptC~",
                        column: x => x.ReceiptConfirmationId,
                        principalTable: "RECEIPT_CONFIRMATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_CONFIRMATION_ITEM_ReceiptConfirmationId_SalesOrderL~",
                table: "RECEIPT_CONFIRMATION_ITEM",
                columns: new[] { "ReceiptConfirmationId", "SalesOrderLineId", "BatchId" },
                unique: true,
                filter: "\"SalesOrderLineId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_CONFIRMATION_ITEM_SalesOrderLineId",
                table: "RECEIPT_CONFIRMATION_ITEM",
                column: "SalesOrderLineId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RECEIPT_CONFIRMATION_ITEM_ACTUAL_QUANTITY",
                table: "RECEIPT_CONFIRMATION_ITEM",
                sql: "\"DamagedQuantity\" >= 0 AND \"ReceivedQuantity\" >= \"DamagedQuantity\" AND \"ExpectedQuantity\" >= \"ReceivedQuantity\" AND \"MissingQuantity\" = \"ExpectedQuantity\" - \"ReceivedQuantity\"");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_CONFIRMATION_SalesOrderId",
                table: "RECEIPT_CONFIRMATION",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_CONFIRMATION_ShipmentId",
                table: "RECEIPT_CONFIRMATION",
                column: "ShipmentId",
                unique: true,
                filter: "\"ShipmentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_CONFIRMATION_QR_LABEL_BatchQrLabelId",
                table: "RECEIPT_CONFIRMATION_QR_LABEL",
                column: "BatchQrLabelId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_CONFIRMATION_QR_LABEL_ReceiptConfirmationId_BatchQr~",
                table: "RECEIPT_CONFIRMATION_QR_LABEL",
                columns: new[] { "ReceiptConfirmationId", "BatchQrLabelId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RECEIPT_CONFIRMATION_SHIPMENT_ShipmentId",
                table: "RECEIPT_CONFIRMATION",
                column: "ShipmentId",
                principalTable: "SHIPMENT",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RECEIPT_CONFIRMATION_ITEM_SALES_ORDER_LINE_SalesOrderLineId",
                table: "RECEIPT_CONFIRMATION_ITEM",
                column: "SalesOrderLineId",
                principalTable: "SALES_ORDER_LINE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RECEIPT_CONFIRMATION_SHIPMENT_ShipmentId",
                table: "RECEIPT_CONFIRMATION");

            migrationBuilder.DropForeignKey(
                name: "FK_RECEIPT_CONFIRMATION_ITEM_SALES_ORDER_LINE_SalesOrderLineId",
                table: "RECEIPT_CONFIRMATION_ITEM");

            migrationBuilder.DropTable(
                name: "RECEIPT_CONFIRMATION_QR_LABEL");

            migrationBuilder.DropIndex(
                name: "IX_RECEIPT_CONFIRMATION_ITEM_ReceiptConfirmationId_SalesOrderL~",
                table: "RECEIPT_CONFIRMATION_ITEM");

            migrationBuilder.DropIndex(
                name: "IX_RECEIPT_CONFIRMATION_ITEM_SalesOrderLineId",
                table: "RECEIPT_CONFIRMATION_ITEM");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RECEIPT_CONFIRMATION_ITEM_ACTUAL_QUANTITY",
                table: "RECEIPT_CONFIRMATION_ITEM");

            migrationBuilder.DropIndex(
                name: "IX_RECEIPT_CONFIRMATION_SalesOrderId",
                table: "RECEIPT_CONFIRMATION");

            migrationBuilder.DropIndex(
                name: "IX_RECEIPT_CONFIRMATION_ShipmentId",
                table: "RECEIPT_CONFIRMATION");

            migrationBuilder.DropColumn(
                name: "DamagedQuantity",
                table: "RECEIPT_CONFIRMATION_ITEM");

            migrationBuilder.DropColumn(
                name: "MissingQuantity",
                table: "RECEIPT_CONFIRMATION_ITEM");

            migrationBuilder.DropColumn(
                name: "SalesOrderLineId",
                table: "RECEIPT_CONFIRMATION_ITEM");

            migrationBuilder.DropColumn(
                name: "ShipmentId",
                table: "RECEIPT_CONFIRMATION");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_CONFIRMATION_ITEM_ReceiptConfirmationId",
                table: "RECEIPT_CONFIRMATION_ITEM",
                column: "ReceiptConfirmationId");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_CONFIRMATION_SalesOrderId",
                table: "RECEIPT_CONFIRMATION",
                column: "SalesOrderId",
                unique: true);
        }
    }
}
