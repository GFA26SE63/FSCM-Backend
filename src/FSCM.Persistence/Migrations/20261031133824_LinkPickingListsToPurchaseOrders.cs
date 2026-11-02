using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkPickingListsToPurchaseOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PICKING_LIST_SALES_ORDER_SalesOrderId",
                table: "PICKING_LIST");

            migrationBuilder.RenameColumn(
                name: "SalesOrderId",
                table: "PICKING_LIST",
                newName: "PurchaseOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_PICKING_LIST_SalesOrderId_WarehouseId",
                table: "PICKING_LIST",
                newName: "IX_PICKING_LIST_PurchaseOrderId_WarehouseId");

            migrationBuilder.Sql("""
                CREATE TABLE "HISTORY_PICKING_LIST_WITHOUT_PO" AS
                SELECT l.* FROM "PICKING_LIST" l
                WHERE NOT EXISTS (SELECT 1 FROM "PURCHASE_ORDER" po WHERE po."SalesOrderId" = l."PurchaseOrderId");
                CREATE TABLE "HISTORY_PICKING_ITEM_WITHOUT_PO" AS
                SELECT i.* FROM "PICKING_ITEM" i
                JOIN "HISTORY_PICKING_LIST_WITHOUT_PO" l ON l."Id" = i."PickingListId";
                UPDATE "SHIPMENT" s SET "PickingListId" = NULL
                WHERE EXISTS (SELECT 1 FROM "HISTORY_PICKING_LIST_WITHOUT_PO" l WHERE l."Id" = s."PickingListId");
                DELETE FROM "PICKING_ITEM" i
                WHERE EXISTS (SELECT 1 FROM "HISTORY_PICKING_LIST_WITHOUT_PO" l WHERE l."Id" = i."PickingListId");
                DELETE FROM "PICKING_LIST" l
                WHERE EXISTS (SELECT 1 FROM "HISTORY_PICKING_LIST_WITHOUT_PO" h WHERE h."Id" = l."Id");
                UPDATE "PICKING_LIST" l SET "PurchaseOrderId" = source."Id"
                FROM (
                    SELECT DISTINCT ON ("SalesOrderId") "Id", "SalesOrderId"
                    FROM "PURCHASE_ORDER" ORDER BY "SalesOrderId", "IssuedAt" DESC, "Id"
                ) source WHERE source."SalesOrderId" = l."PurchaseOrderId";
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_PICKING_LIST_PURCHASE_ORDER_PurchaseOrderId",
                table: "PICKING_LIST",
                column: "PurchaseOrderId",
                principalTable: "PURCHASE_ORDER",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Length > 0)
                throw new NotSupportedException("Forward-only migration: picking history cannot be reconstructed safely.");
            migrationBuilder.DropForeignKey(
                name: "FK_PICKING_LIST_PURCHASE_ORDER_PurchaseOrderId",
                table: "PICKING_LIST");

            migrationBuilder.RenameColumn(
                name: "PurchaseOrderId",
                table: "PICKING_LIST",
                newName: "SalesOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_PICKING_LIST_PurchaseOrderId_WarehouseId",
                table: "PICKING_LIST",
                newName: "IX_PICKING_LIST_SalesOrderId_WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_PICKING_LIST_SALES_ORDER_SalesOrderId",
                table: "PICKING_LIST",
                column: "SalesOrderId",
                principalTable: "SALES_ORDER",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
