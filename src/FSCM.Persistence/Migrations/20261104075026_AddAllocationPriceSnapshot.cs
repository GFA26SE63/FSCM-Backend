using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAllocationPriceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PostPromotionUnitPrice",
                table: "ALLOCATION",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PricedAt",
                table: "ALLOCATION",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingExplanation",
                table: "ALLOCATION",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RemainingShelfLifePercent",
                table: "ALLOCATION",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShelfLifeDiscountPerUnit",
                table: "ALLOCATION",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShelfLifeDiscountTableVersion",
                table: "ALLOCATION",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShelfLifeDiscountTierId",
                table: "ALLOCATION",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShelfLifeDiscountType",
                table: "ALLOCATION",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShelfLifeDiscountValue",
                table: "ALLOCATION",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PostPromotionUnitPrice",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "PricedAt",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "PricingExplanation",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "RemainingShelfLifePercent",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "ShelfLifeDiscountPerUnit",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "ShelfLifeDiscountTableVersion",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "ShelfLifeDiscountTierId",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "ShelfLifeDiscountType",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "ShelfLifeDiscountValue",
                table: "ALLOCATION");
        }
    }
}
