using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceInventoryLifecycleIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_STOCK_HOLD_AllocationId",
                table: "STOCK_HOLD");

            migrationBuilder.DropIndex(
                name: "IX_RESERVATION_AllocationId",
                table: "RESERVATION");

            migrationBuilder.CreateIndex(
                name: "IX_STOCK_HOLD_AllocationId",
                table: "STOCK_HOLD",
                column: "AllocationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RESERVATION_AllocationId",
                table: "RESERVATION",
                column: "AllocationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_STOCK_HOLD_AllocationId",
                table: "STOCK_HOLD");

            migrationBuilder.DropIndex(
                name: "IX_RESERVATION_AllocationId",
                table: "RESERVATION");

            migrationBuilder.CreateIndex(
                name: "IX_STOCK_HOLD_AllocationId",
                table: "STOCK_HOLD",
                column: "AllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RESERVATION_AllocationId",
                table: "RESERVATION",
                column: "AllocationId");
        }
    }
}
