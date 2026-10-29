using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepointShipmentToRunPickingList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_PICKING_LIST_PickingListId",
                table: "SHIPMENT");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_PickingListId",
                table: "SHIPMENT");

            migrationBuilder.RenameColumn(
                name: "PickingListId",
                table: "SHIPMENT",
                newName: "RunPickingListId");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_RunPickingListId",
                table: "SHIPMENT",
                column: "RunPickingListId");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_RUN_PICKING_LIST_RunPickingListId",
                table: "SHIPMENT",
                column: "RunPickingListId",
                principalTable: "RUN_PICKING_LIST",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_RUN_PICKING_LIST_RunPickingListId",
                table: "SHIPMENT");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_RunPickingListId",
                table: "SHIPMENT");

            migrationBuilder.RenameColumn(
                name: "RunPickingListId",
                table: "SHIPMENT",
                newName: "PickingListId");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_PickingListId",
                table: "SHIPMENT",
                column: "PickingListId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_PICKING_LIST_PickingListId",
                table: "SHIPMENT",
                column: "PickingListId",
                principalTable: "PICKING_LIST",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
