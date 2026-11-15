using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseDocumentsAndCartons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_INVENTORY_TRANSACTION_TransactionType",
                table: "INVENTORY_TRANSACTION");

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_DOCUMENT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationWarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    GoodsReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    OperationKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_DOCUMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_DOCUMENT_GOODS_RECEIPT_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "GOODS_RECEIPT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_DOCUMENT_WAREHOUSE_DOCUMENT_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "WAREHOUSE_DOCUMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_DOCUMENT_WAREHOUSE_DestinationWarehouseId",
                        column: x => x.DestinationWarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_DOCUMENT_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_DOCUMENT_LINE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    PickingItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    PickingListId = table.Column<Guid>(type: "uuid", nullable: true),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderCode = table.Column<string>(type: "text", nullable: true),
                    Sku = table.Column<string>(type: "text", nullable: false),
                    ProductName = table.Column<string>(type: "text", nullable: false),
                    BatchCode = table.Column<string>(type: "text", nullable: true),
                    ProductionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    SourceLotCode = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_DOCUMENT_LINE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_DOCUMENT_LINE_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_DOCUMENT_LINE_PICKING_ITEM_PickingItemId",
                        column: x => x.PickingItemId,
                        principalTable: "PICKING_ITEM",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_DOCUMENT_LINE_PICKING_LIST_PickingListId",
                        column: x => x.PickingListId,
                        principalTable: "PICKING_LIST",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_DOCUMENT_LINE_PRODUCT_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PRODUCT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_DOCUMENT_LINE_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_DOCUMENT_LINE_WAREHOUSE_DOCUMENT_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "WAREHOUSE_DOCUMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_DOCUMENT_CARTON",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    CartonId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    LabelId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScannedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_DOCUMENT_CARTON", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_DOCUMENT_CARTON_BATCH_QR_LABEL_LabelId",
                        column: x => x.LabelId,
                        principalTable: "BATCH_QR_LABEL",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_DOCUMENT_CARTON_WAREHOUSE_DOCUMENT_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "WAREHOUSE_DOCUMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_DOCUMENT_CARTON_WAREHOUSE_DOCUMENT_LINE_LineId",
                        column: x => x.LineId,
                        principalTable: "WAREHOUSE_DOCUMENT_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_INVENTORY_TRANSACTION_TransactionType",
                table: "INVENTORY_TRANSACTION",
                sql: "\"TransactionType\" BETWEEN 0 AND 10");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_DOCUMENT_Code",
                table: "WAREHOUSE_DOCUMENT",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_DOCUMENT_DestinationWarehouseId",
                table: "WAREHOUSE_DOCUMENT",
                column: "DestinationWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_DOCUMENT_GoodsReceiptId",
                table: "WAREHOUSE_DOCUMENT",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_DOCUMENT_OperationKey",
                table: "WAREHOUSE_DOCUMENT",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_DOCUMENT_SourceDocumentId",
                table: "WAREHOUSE_DOCUMENT",
                column: "SourceDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_DOCUMENT_WarehouseId",
                table: "WAREHOUSE_DOCUMENT",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_DOCUMENT_CARTON_DocumentId_CartonId",
                table: "WAREHOUSE_DOCUMENT_CARTON",
                columns: new[] { "DocumentId", "CartonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_DOCUMENT_CARTON_LabelId",
                table: "WAREHOUSE_DOCUMENT_CARTON",
                column: "LabelId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_DOCUMENT_CARTON_LineId",
                table: "WAREHOUSE_DOCUMENT_CARTON",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_DOCUMENT_LINE_BatchId",
                table: "WAREHOUSE_DOCUMENT_LINE",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_DOCUMENT_LINE_DocumentId_PickingItemId",
                table: "WAREHOUSE_DOCUMENT_LINE",
                columns: new[] { "DocumentId", "PickingItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_DOCUMENT_LINE_PickingItemId",
                table: "WAREHOUSE_DOCUMENT_LINE",
                column: "PickingItemId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_DOCUMENT_LINE_PickingListId",
                table: "WAREHOUSE_DOCUMENT_LINE",
                column: "PickingListId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_DOCUMENT_LINE_ProductId",
                table: "WAREHOUSE_DOCUMENT_LINE",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_DOCUMENT_LINE_SalesOrderId",
                table: "WAREHOUSE_DOCUMENT_LINE",
                column: "SalesOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WAREHOUSE_DOCUMENT_CARTON");

            migrationBuilder.DropTable(
                name: "WAREHOUSE_DOCUMENT_LINE");

            migrationBuilder.DropTable(
                name: "WAREHOUSE_DOCUMENT");

            migrationBuilder.DropCheckConstraint(
                name: "CK_INVENTORY_TRANSACTION_TransactionType",
                table: "INVENTORY_TRANSACTION");

            migrationBuilder.AddCheckConstraint(
                name: "CK_INVENTORY_TRANSACTION_TransactionType",
                table: "INVENTORY_TRANSACTION",
                sql: "\"TransactionType\" BETWEEN 0 AND 8");
        }
    }
}
