using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistedOrderWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SALES_ORDER_LINE_SalesOrderId",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropIndex(
                name: "IX_SALES_ORDER_CreatedByUserId",
                table: "SALES_ORDER");

            migrationBuilder.DropIndex(
                name: "IX_ALLOCATION_SalesOrderLineId",
                table: "ALLOCATION");

            migrationBuilder.CreateSequence(
                name: "SalesOrderCodeSequence",
                startValue: 1030L);

            migrationBuilder.Sql(
                """
                SELECT setval(
                    '"SalesOrderCodeSequence"',
                    GREATEST(
                        1029,
                        COALESCE(MAX(
                            CASE
                                WHEN "OrderCode" ~ '^DH-[0-9]{6}$'
                                THEN SUBSTRING("OrderCode" FROM 4)::bigint
                            END), 1029)),
                    true)
                FROM "SALES_ORDER";
                """);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                table: "SALES_ORDER_LINE",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DiscountSourceCode",
                table: "SALES_ORDER_LINE",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscountSourceType",
                table: "SALES_ORDER_LINE",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LineNumber",
                table: "SALES_ORDER_LINE",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProductNameSnapshot",
                table: "SALES_ORDER_LINE",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SkuSnapshot",
                table: "SALES_ORDER_LINE",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AllocationTraceJson",
                table: "SALES_ORDER",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<int>(
                name: "CreditTermDays",
                table: "SALES_ORDER",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "SALES_ORDER",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "SALES_ORDER",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "RequestedDeliveryDate",
                table: "SALES_ORDER",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetailerCodeSnapshot",
                table: "SALES_ORDER",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RetailerNameSnapshot",
                table: "SALES_ORDER",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "SALES_ORDER",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "UserAccountId",
                table: "RETAILER",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CoverageScore",
                table: "ALLOCATION",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "ALLOCATION",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                table: "ALLOCATION",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "DiscountSourceCode",
                table: "ALLOCATION",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscountSourceType",
                table: "ALLOCATION",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DistanceScore",
                table: "ALLOCATION",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossAmount",
                table: "ALLOCATION",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetAmount",
                table: "ALLOCATION",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PromotionId",
                table: "ALLOCATION",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShelfLifeScore",
                table: "ALLOCATION",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "ALLOCATION",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightedScore",
                table: "ALLOCATION",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(
                """
                WITH numbered_lines AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "SalesOrderId"
                               ORDER BY "CreatedAt", "Id") AS line_number
                    FROM "SALES_ORDER_LINE"
                )
                UPDATE "SALES_ORDER_LINE" AS line
                SET "LineNumber" = numbered_lines.line_number,
                    "SkuSnapshot" = product."Sku",
                    "ProductNameSnapshot" = product."Name"
                FROM numbered_lines, "PRODUCT" AS product
                WHERE line."Id" = numbered_lines."Id"
                  AND product."Id" = line."ProductId";

                UPDATE "SALES_ORDER" AS sales_order
                SET "RetailerCodeSnapshot" = retailer."Code",
                    "RetailerNameSnapshot" = retailer."Name",
                    "RequestHash" = 'legacy-' || REPLACE(sales_order."Id"::text, '-', '')
                FROM "RETAILER" AS retailer
                WHERE retailer."Id" = sales_order."RetailerId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_LINE_SalesOrderId_LineNumber",
                table: "SALES_ORDER_LINE",
                columns: new[] { "SalesOrderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_CreatedByUserId_ServerReceivedAt",
                table: "SALES_ORDER",
                columns: new[] { "CreatedByUserId", "ServerReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_UserAccountId",
                table: "RETAILER",
                column: "UserAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_PromotionId",
                table: "ALLOCATION",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_SalesOrderLineId_AllocationSequence",
                table: "ALLOCATION",
                columns: new[] { "SalesOrderLineId", "AllocationSequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ALLOCATION_PROMOTION_PromotionId",
                table: "ALLOCATION",
                column: "PromotionId",
                principalTable: "PROMOTION",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RETAILER_USER_ACCOUNT_UserAccountId",
                table: "RETAILER",
                column: "UserAccountId",
                principalTable: "USER_ACCOUNT",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ALLOCATION_PROMOTION_PromotionId",
                table: "ALLOCATION");

            migrationBuilder.DropForeignKey(
                name: "FK_RETAILER_USER_ACCOUNT_UserAccountId",
                table: "RETAILER");

            migrationBuilder.DropIndex(
                name: "IX_SALES_ORDER_LINE_SalesOrderId_LineNumber",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropIndex(
                name: "IX_SALES_ORDER_CreatedByUserId_ServerReceivedAt",
                table: "SALES_ORDER");

            migrationBuilder.DropIndex(
                name: "IX_RETAILER_UserAccountId",
                table: "RETAILER");

            migrationBuilder.DropIndex(
                name: "IX_ALLOCATION_PromotionId",
                table: "ALLOCATION");

            migrationBuilder.DropIndex(
                name: "IX_ALLOCATION_SalesOrderLineId_AllocationSequence",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "DiscountSourceCode",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "DiscountSourceType",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "LineNumber",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "ProductNameSnapshot",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "SkuSnapshot",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "AllocationTraceJson",
                table: "SALES_ORDER");

            migrationBuilder.DropColumn(
                name: "CreditTermDays",
                table: "SALES_ORDER");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "SALES_ORDER");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "SALES_ORDER");

            migrationBuilder.DropColumn(
                name: "RequestedDeliveryDate",
                table: "SALES_ORDER");

            migrationBuilder.DropColumn(
                name: "RetailerCodeSnapshot",
                table: "SALES_ORDER");

            migrationBuilder.DropColumn(
                name: "RetailerNameSnapshot",
                table: "SALES_ORDER");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "SALES_ORDER");

            migrationBuilder.DropColumn(
                name: "UserAccountId",
                table: "RETAILER");

            migrationBuilder.DropColumn(
                name: "CoverageScore",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "DiscountSourceCode",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "DiscountSourceType",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "DistanceScore",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "GrossAmount",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "NetAmount",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "PromotionId",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "ShelfLifeScore",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "ALLOCATION");

            migrationBuilder.DropColumn(
                name: "WeightedScore",
                table: "ALLOCATION");

            migrationBuilder.DropSequence(
                name: "SalesOrderCodeSequence");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_LINE_SalesOrderId",
                table: "SALES_ORDER_LINE",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_CreatedByUserId",
                table: "SALES_ORDER",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_SalesOrderLineId",
                table: "ALLOCATION",
                column: "SalesOrderLineId");
        }
    }
}
