using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptBatchInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BATCH_WarehouseId",
                table: "BATCH");

            migrationBuilder.RenameColumn(
                name: "ExpirationAt",
                table: "GOODS_RECEIPT_LINE",
                newName: "ExpirationDate");

            migrationBuilder.RenameColumn(
                name: "ExpirationAt",
                table: "BATCH",
                newName: "ExpirationDate");

            migrationBuilder.Sql("""
                ALTER TABLE "GOODS_RECEIPT_LINE"
                ALTER COLUMN "ExpirationDate" TYPE date
                USING (("ExpirationDate" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date);
                ALTER TABLE "BATCH"
                ALTER COLUMN "ExpirationDate" TYPE date
                USING (("ExpirationDate" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date);
                """);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "GOODS_RECEIPT",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "GOODS_RECEIPT",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BUSINESS_DOCUMENT_SEQUENCE",
                columns: table => new
                {
                    DocumentType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LastNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BUSINESS_DOCUMENT_SEQUENCE", x => new { x.DocumentType, x.BusinessDate });
                    table.CheckConstraint("CK_BUSINESS_DOCUMENT_SEQUENCE_LastNumber", "\"LastNumber\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "INVENTORY_TRANSACTION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoodsReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
                    GoodsReceiptLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReferenceCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PhysicalQuantityDelta = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    HeldQuantityDelta = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReservedQuantityDelta = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    PhysicalQuantityAfter = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    HeldQuantityAfter = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReservedQuantityAfter = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    AvailableQuantityAfter = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INVENTORY_TRANSACTION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_INVENTORY_TRANSACTION_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_INVENTORY_TRANSACTION_GOODS_RECEIPT_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "GOODS_RECEIPT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_INVENTORY_TRANSACTION_GOODS_RECEIPT_LINE_GoodsReceiptLineId",
                        column: x => x.GoodsReceiptLineId,
                        principalTable: "GOODS_RECEIPT_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_INVENTORY_TRANSACTION_PRODUCT_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PRODUCT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_INVENTORY_TRANSACTION_USER_ACCOUNT_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_INVENTORY_TRANSACTION_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_INVENTORY_BALANCE_AvailableQuantity",
                table: "INVENTORY_BALANCE",
                sql: "\"AvailableQuantity\" = \"PhysicalQuantity\" - \"HeldQuantity\" - \"ReservedQuantity\" AND \"AvailableQuantity\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_INVENTORY_BALANCE_HeldQuantity",
                table: "INVENTORY_BALANCE",
                sql: "\"HeldQuantity\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_INVENTORY_BALANCE_PhysicalQuantity",
                table: "INVENTORY_BALANCE",
                sql: "\"PhysicalQuantity\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_INVENTORY_BALANCE_ReservedQuantity",
                table: "INVENTORY_BALANCE",
                sql: "\"ReservedQuantity\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GOODS_RECEIPT_LINE_ReceivedQuantity",
                table: "GOODS_RECEIPT_LINE",
                sql: "\"ReceivedQuantity\" > 0 AND TRUNC(\"ReceivedQuantity\") = \"ReceivedQuantity\"");

            migrationBuilder.CreateIndex(
                name: "IX_GOODS_RECEIPT_IdempotencyKey",
                table: "GOODS_RECEIPT",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_WarehouseId_ProductId_ExpirationDate",
                table: "BATCH",
                columns: new[] { "WarehouseId", "ProductId", "ExpirationDate" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BATCH_Quantity",
                table: "BATCH",
                sql: "\"Quantity\" >= 0 AND TRUNC(\"Quantity\") = \"Quantity\"");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_TRANSACTION_BatchId_OccurredAt",
                table: "INVENTORY_TRANSACTION",
                columns: new[] { "BatchId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_TRANSACTION_CreatedByUserId",
                table: "INVENTORY_TRANSACTION",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_TRANSACTION_GoodsReceiptId",
                table: "INVENTORY_TRANSACTION",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_TRANSACTION_GoodsReceiptLineId",
                table: "INVENTORY_TRANSACTION",
                column: "GoodsReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_TRANSACTION_ProductId",
                table: "INVENTORY_TRANSACTION",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_TRANSACTION_WarehouseId",
                table: "INVENTORY_TRANSACTION",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BUSINESS_DOCUMENT_SEQUENCE");

            migrationBuilder.DropTable(
                name: "INVENTORY_TRANSACTION");

            migrationBuilder.DropCheckConstraint(
                name: "CK_INVENTORY_BALANCE_AvailableQuantity",
                table: "INVENTORY_BALANCE");

            migrationBuilder.DropCheckConstraint(
                name: "CK_INVENTORY_BALANCE_HeldQuantity",
                table: "INVENTORY_BALANCE");

            migrationBuilder.DropCheckConstraint(
                name: "CK_INVENTORY_BALANCE_PhysicalQuantity",
                table: "INVENTORY_BALANCE");

            migrationBuilder.DropCheckConstraint(
                name: "CK_INVENTORY_BALANCE_ReservedQuantity",
                table: "INVENTORY_BALANCE");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GOODS_RECEIPT_LINE_ReceivedQuantity",
                table: "GOODS_RECEIPT_LINE");

            migrationBuilder.DropIndex(
                name: "IX_GOODS_RECEIPT_IdempotencyKey",
                table: "GOODS_RECEIPT");

            migrationBuilder.DropIndex(
                name: "IX_BATCH_WarehouseId_ProductId_ExpirationDate",
                table: "BATCH");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BATCH_Quantity",
                table: "BATCH");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "GOODS_RECEIPT");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "GOODS_RECEIPT");

            migrationBuilder.Sql("""
                ALTER TABLE "GOODS_RECEIPT_LINE"
                ALTER COLUMN "ExpirationDate" TYPE timestamp with time zone
                USING (("ExpirationDate"::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh'));
                ALTER TABLE "BATCH"
                ALTER COLUMN "ExpirationDate" TYPE timestamp with time zone
                USING (("ExpirationDate"::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh'));
                """);

            migrationBuilder.RenameColumn(
                name: "ExpirationDate",
                table: "GOODS_RECEIPT_LINE",
                newName: "ExpirationAt");

            migrationBuilder.RenameColumn(
                name: "ExpirationDate",
                table: "BATCH",
                newName: "ExpirationAt");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_WarehouseId",
                table: "BATCH",
                column: "WarehouseId");
        }
    }
}
