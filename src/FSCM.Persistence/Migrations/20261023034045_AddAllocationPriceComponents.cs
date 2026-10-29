using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAllocationPriceComponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ListUnitPrice",
                table: "ALLOCATION_RUN_LINE",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BatchExpiresAt",
                table: "ALLOCATION_PLAN_PRICE",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<decimal>(
                name: "ListUnitPrice",
                table: "ALLOCATION_PLAN_PRICE",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PricedAt",
                table: "ALLOCATION_PLAN_PRICE",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "TotalShelfLifeHours",
                table: "ALLOCATION_PLAN_PRICE",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ListUnitPrice",
                table: "ALLOCATION_RUN_LINE");

            migrationBuilder.DropColumn(
                name: "BatchExpiresAt",
                table: "ALLOCATION_PLAN_PRICE");

            migrationBuilder.DropColumn(
                name: "ListUnitPrice",
                table: "ALLOCATION_PLAN_PRICE");

            migrationBuilder.DropColumn(
                name: "PricedAt",
                table: "ALLOCATION_PLAN_PRICE");

            migrationBuilder.DropColumn(
                name: "TotalShelfLifeHours",
                table: "ALLOCATION_PLAN_PRICE");
        }
    }
}
