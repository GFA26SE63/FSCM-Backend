using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecurringThreeHourAllocationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ALLOCATION_RUN_BusinessDate",
                table: "ALLOCATION_RUN");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_BusinessDate",
                table: "ALLOCATION_RUN",
                column: "BusinessDate");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_CutoffAt",
                table: "ALLOCATION_RUN",
                column: "CutoffAt",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ALLOCATION_RUN_BusinessDate",
                table: "ALLOCATION_RUN");

            migrationBuilder.DropIndex(
                name: "IX_ALLOCATION_RUN_CutoffAt",
                table: "ALLOCATION_RUN");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_BusinessDate",
                table: "ALLOCATION_RUN",
                column: "BusinessDate",
                unique: true);
        }
    }
}
