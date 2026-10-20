using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPickingWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PICKING_LIST_SalesOrderId",
                table: "PICKING_LIST");

            migrationBuilder.DropIndex(
                name: "IX_PICKING_ITEM_AllocationId",
                table: "PICKING_ITEM");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "PICKING_LIST",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_LIST_SalesOrderId_WarehouseId",
                table: "PICKING_LIST",
                columns: new[] { "SalesOrderId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_ITEM_AllocationId",
                table: "PICKING_ITEM",
                column: "AllocationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PICKING_LIST_SalesOrderId_WarehouseId",
                table: "PICKING_LIST");

            migrationBuilder.DropIndex(
                name: "IX_PICKING_ITEM_AllocationId",
                table: "PICKING_ITEM");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "PICKING_LIST");

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_LIST_SalesOrderId",
                table: "PICKING_LIST",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PICKING_ITEM_AllocationId",
                table: "PICKING_ITEM",
                column: "AllocationId");
        }
    }
}
