using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryDefaultMlor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DefaultMlorRatio",
                table: "PRODUCT_CATEGORY",
                type: "numeric(6,5)",
                precision: 6,
                scale: 5,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DefaultMlorUpdatedAt",
                table: "PRODUCT_CATEGORY",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultMlorVersion",
                table: "PRODUCT_CATEGORY",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PRODUCT_CATEGORY_DEFAULT_MLOR",
                table: "PRODUCT_CATEGORY",
                sql: "\"DefaultMlorRatio\" IS NULL OR (\"DefaultMlorRatio\" > 0 AND \"DefaultMlorRatio\" < 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PRODUCT_CATEGORY_DEFAULT_MLOR",
                table: "PRODUCT_CATEGORY");

            migrationBuilder.DropColumn(
                name: "DefaultMlorRatio",
                table: "PRODUCT_CATEGORY");

            migrationBuilder.DropColumn(
                name: "DefaultMlorUpdatedAt",
                table: "PRODUCT_CATEGORY");

            migrationBuilder.DropColumn(
                name: "DefaultMlorVersion",
                table: "PRODUCT_CATEGORY");
        }
    }
}
