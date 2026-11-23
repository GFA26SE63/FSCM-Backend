using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentAssignmentStatusIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_DriverId",
                table: "SHIPMENT");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_VehicleId",
                table: "SHIPMENT");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_DriverId_Status",
                table: "SHIPMENT",
                columns: new[] { "DriverId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_VehicleId_Status",
                table: "SHIPMENT",
                columns: new[] { "VehicleId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_DriverId_Status",
                table: "SHIPMENT");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_VehicleId_Status",
                table: "SHIPMENT");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_DriverId",
                table: "SHIPMENT",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_VehicleId",
                table: "SHIPMENT",
                column: "VehicleId");
        }
    }
}
