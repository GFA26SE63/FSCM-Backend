using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddS1COrderCaptureReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FinalPricePendingAllocation",
                table: "SALES_ORDER_LINE",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "FinalPricePendingAllocation",
                table: "SALES_ORDER",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinalPricePendingAllocation",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "FinalPricePendingAllocation",
                table: "SALES_ORDER");
        }
    }
}
