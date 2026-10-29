using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteS1DGoodsReceiptApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ProductionDate",
                table: "GOODS_RECEIPT_LINE",
                type: "date",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "GOODS_RECEIPT_LINE" AS line
                SET "ProductionDate" = line."ExpirationDate"
                    - GREATEST(1, CEIL(product."ShelfLifeHours" / 24.0)::integer)
                FROM "PRODUCT" AS product
                WHERE product."Id" = line."ProductId";

                UPDATE "GOODS_RECEIPT_LINE"
                SET "ProductionDate" = "ExpirationDate" - 1
                WHERE "ProductionDate" IS NULL;
                """);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "ProductionDate",
                table: "GOODS_RECEIPT_LINE",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "GOODS_RECEIPT",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_GOODS_RECEIPT_LINE_Dates",
                table: "GOODS_RECEIPT_LINE",
                sql: "\"ProductionDate\" < \"ExpirationDate\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GOODS_RECEIPT_Status",
                table: "GOODS_RECEIPT",
                sql: "\"Status\" IN ('Pending', 'Received')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_GOODS_RECEIPT_LINE_Dates",
                table: "GOODS_RECEIPT_LINE");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GOODS_RECEIPT_Status",
                table: "GOODS_RECEIPT");

            migrationBuilder.DropColumn(
                name: "ProductionDate",
                table: "GOODS_RECEIPT_LINE");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "GOODS_RECEIPT");
        }
    }
}
