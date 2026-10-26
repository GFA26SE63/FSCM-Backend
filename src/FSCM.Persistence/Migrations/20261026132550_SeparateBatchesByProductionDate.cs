using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparateBatchesByProductionDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BATCH_WarehouseId_ProductId_ExpirationDate",
                table: "BATCH");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ProductionDate",
                table: "BATCH",
                type: "date",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "BATCH" AS batch
                SET "ProductionDate" = COALESCE(receipt_line."ProductionDate", batch."ExpirationDate" - 1)
                FROM "GOODS_RECEIPT_LINE" AS receipt_line
                WHERE receipt_line."Id" = batch."GoodsReceiptLineId";

                UPDATE "BATCH"
                SET "ProductionDate" = "ExpirationDate" - 1
                WHERE "ProductionDate" IS NULL;
                """);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "ProductionDate",
                table: "BATCH",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.Sql("""
                UPDATE "BATCH" AS batch
                SET "BatchCode" = warehouse."Code" || '-' || product."Sku" || '-' ||
                    TO_CHAR(batch."ProductionDate", 'DDMMYYYY') || '-' ||
                    TO_CHAR(batch."ExpirationDate", 'DDMMYYYY')
                FROM "WAREHOUSE" AS warehouse, "PRODUCT" AS product
                WHERE warehouse."Id" = batch."WarehouseId"
                  AND product."Id" = batch."ProductId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_WarehouseId_ProductId_ProductionDate_ExpirationDate",
                table: "BATCH",
                columns: new[] { "WarehouseId", "ProductId", "ProductionDate", "ExpirationDate" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BATCH_Dates",
                table: "BATCH",
                sql: "\"ProductionDate\" < \"ExpirationDate\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BATCH_WarehouseId_ProductId_ProductionDate_ExpirationDate",
                table: "BATCH");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BATCH_Dates",
                table: "BATCH");

            migrationBuilder.Sql("""
                UPDATE "BATCH" AS batch
                SET "BatchCode" = warehouse."Code" || '-' || product."Sku" || '-' ||
                    TO_CHAR(batch."ExpirationDate", 'DDMMYYYY')
                FROM "WAREHOUSE" AS warehouse, "PRODUCT" AS product
                WHERE warehouse."Id" = batch."WarehouseId"
                  AND product."Id" = batch."ProductId";
                """);

            migrationBuilder.DropColumn(
                name: "ProductionDate",
                table: "BATCH");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_WarehouseId_ProductId_ExpirationDate",
                table: "BATCH",
                columns: new[] { "WarehouseId", "ProductId", "ExpirationDate" },
                unique: true);
        }
    }
}
