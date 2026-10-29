using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteS1COrderPricingSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MlorMinimumLifeHours",
                table: "SALES_ORDER_LINE",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MlorMinimumLifeRatio",
                table: "SALES_ORDER_LINE",
                type: "numeric(6,5)",
                precision: 6,
                scale: 5,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "MlorProductCategoryId",
                table: "SALES_ORDER_LINE",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MlorResolvedAt",
                table: "SALES_ORDER_LINE",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "MlorSourceRuleId",
                table: "SALES_ORDER_LINE",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MlorSourceScope",
                table: "SALES_ORDER_LINE",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MlorSourceVersion",
                table: "SALES_ORDER_LINE",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PricedAt",
                table: "SALES_ORDER_LINE",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProductPriceEffectiveFrom",
                table: "SALES_ORDER_LINE",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProductPriceEffectiveTo",
                table: "SALES_ORDER_LINE",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductPriceId",
                table: "SALES_ORDER_LINE",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EffectiveFromSnapshot",
                table: "ORDER_PROMOTION",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EffectiveToSnapshot",
                table: "ORDER_PROMOTION",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumQuantitySnapshot",
                table: "ORDER_PROMOTION",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PromotionCodeSnapshot",
                table: "ORDER_PROMOTION",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PromotionNameSnapshot",
                table: "ORDER_PROMOTION",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PromotionTypeSnapshot",
                table: "ORDER_PROMOTION",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "ORDER_PROMOTION" AS op
                SET "PromotionCodeSnapshot" = p."Code",
                    "PromotionNameSnapshot" = p."Name",
                    "PromotionTypeSnapshot" = p."PromotionType",
                    "MinimumQuantitySnapshot" = p."MinimumQuantity",
                    "EffectiveFromSnapshot" = p."StartAt",
                    "EffectiveToSnapshot" = p."EndAt"
                FROM "PROMOTION" AS p
                WHERE p."Id" = op."PromotionId";

                WITH price_snapshot AS (
                    SELECT line."Id" AS "LineId", price."Id", price."EffectiveFrom", price."EffectiveTo"
                    FROM "SALES_ORDER_LINE" AS line
                    JOIN LATERAL (
                        SELECT pp."Id", pp."EffectiveFrom", pp."EffectiveTo"
                        FROM "PRODUCT_PRICE" AS pp
                        WHERE pp."ProductId" = line."ProductId"
                          AND pp."EffectiveFrom" <= line."CreatedAt"
                          AND (pp."EffectiveTo" IS NULL OR pp."EffectiveTo" > line."CreatedAt")
                        ORDER BY pp."EffectiveFrom" DESC
                        LIMIT 1
                    ) AS price ON TRUE
                )
                UPDATE "SALES_ORDER_LINE" AS line
                SET "ProductPriceId" = price."Id",
                    "ProductPriceEffectiveFrom" = price."EffectiveFrom",
                    "ProductPriceEffectiveTo" = price."EffectiveTo",
                    "PricedAt" = line."CreatedAt"
                FROM price_snapshot AS price
                WHERE price."LineId" = line."Id";

                WITH mlor_snapshot AS (
                    SELECT line."Id" AS "LineId", config."Version", config."ConfigValue"
                    FROM "SALES_ORDER_LINE" AS line
                    JOIN LATERAL (
                        SELECT sc."Version", sc."ConfigValue"
                        FROM "SYSTEM_CONFIGURATION" AS sc
                        WHERE sc."ConfigKey" = 'mlor.default_ratio'
                          AND sc."EffectiveFrom" <= line."CreatedAt"
                        ORDER BY sc."Version" DESC
                        LIMIT 1
                    ) AS config ON TRUE
                )
                UPDATE "SALES_ORDER_LINE" AS line
                SET "MlorSourceScope" = 'Default',
                    "MlorSourceVersion" = config."Version",
                    "MlorMinimumLifeRatio" = (config."ConfigValue" #>> '{}')::numeric,
                    "MlorResolvedAt" = line."CreatedAt"
                FROM mlor_snapshot AS config
                WHERE config."LineId" = line."Id";

                WITH RECURSIVE category_ancestors AS (
                    SELECT line."Id" AS "LineId", sales_order."RetailerId", line."CreatedAt",
                           product."ProductCategoryId" AS "CategoryId", 0 AS "Depth"
                    FROM "SALES_ORDER_LINE" AS line
                    JOIN "SALES_ORDER" AS sales_order ON sales_order."Id" = line."SalesOrderId"
                    JOIN "PRODUCT" AS product ON product."Id" = line."ProductId"
                    UNION ALL
                    SELECT ancestor."LineId", ancestor."RetailerId", ancestor."CreatedAt",
                           category."ParentCategoryId", ancestor."Depth" + 1
                    FROM category_ancestors AS ancestor
                    JOIN "PRODUCT_CATEGORY" AS category ON category."Id" = ancestor."CategoryId"
                    WHERE category."ParentCategoryId" IS NOT NULL
                ), category_snapshot AS (
                    SELECT DISTINCT ON (ancestor."LineId") ancestor."LineId", rule."Id", rule."Version",
                           rule."ProductCategoryId", rule."MinimumLifeRatio", rule."MinimumLifeHours"
                    FROM category_ancestors AS ancestor
                    JOIN "RETAILER_PRODUCT_MLOR" AS rule
                      ON rule."RetailerId" = ancestor."RetailerId"
                     AND rule."ProductCategoryId" = ancestor."CategoryId"
                     AND rule."ProductId" IS NULL
                     AND rule."IsActive"
                     AND rule."EffectiveFrom" <= ancestor."CreatedAt"
                     AND (rule."EffectiveTo" IS NULL OR rule."EffectiveTo" > ancestor."CreatedAt")
                    ORDER BY ancestor."LineId", ancestor."Depth", rule."Version" DESC
                )
                UPDATE "SALES_ORDER_LINE" AS line
                SET "MlorSourceScope" = 'Category',
                    "MlorSourceRuleId" = snapshot."Id",
                    "MlorSourceVersion" = snapshot."Version",
                    "MlorProductCategoryId" = snapshot."ProductCategoryId",
                    "MlorMinimumLifeRatio" = snapshot."MinimumLifeRatio",
                    "MlorMinimumLifeHours" = snapshot."MinimumLifeHours"
                FROM category_snapshot AS snapshot
                WHERE snapshot."LineId" = line."Id";

                WITH sku_snapshot AS (
                    SELECT DISTINCT ON (line."Id") line."Id" AS "LineId", rule."Id", rule."Version",
                           rule."MinimumLifeRatio", rule."MinimumLifeHours"
                    FROM "SALES_ORDER_LINE" AS line
                    JOIN "SALES_ORDER" AS sales_order ON sales_order."Id" = line."SalesOrderId"
                    JOIN "RETAILER_PRODUCT_MLOR" AS rule
                      ON rule."RetailerId" = sales_order."RetailerId"
                     AND rule."ProductId" = line."ProductId"
                     AND rule."ProductCategoryId" IS NULL
                     AND rule."IsActive"
                     AND rule."EffectiveFrom" <= line."CreatedAt"
                     AND (rule."EffectiveTo" IS NULL OR rule."EffectiveTo" > line."CreatedAt")
                    ORDER BY line."Id", rule."Version" DESC
                )
                UPDATE "SALES_ORDER_LINE" AS line
                SET "MlorSourceScope" = 'Sku',
                    "MlorSourceRuleId" = snapshot."Id",
                    "MlorSourceVersion" = snapshot."Version",
                    "MlorProductCategoryId" = NULL,
                    "MlorMinimumLifeRatio" = snapshot."MinimumLifeRatio",
                    "MlorMinimumLifeHours" = snapshot."MinimumLifeHours"
                FROM sku_snapshot AS snapshot
                WHERE snapshot."LineId" = line."Id";

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "SALES_ORDER_LINE"
                        WHERE "ProductPriceId" = '00000000-0000-0000-0000-000000000000'::uuid
                           OR "MlorMinimumLifeRatio" <= 0
                           OR "MlorMinimumLifeRatio" >= 1
                    ) THEN
                        RAISE EXCEPTION 'Cannot backfill S1-C price/MLOR snapshots for every existing order line';
                    END IF;
                END $$;

                ALTER TABLE "SALES_ORDER_LINE" ALTER COLUMN "ProductPriceId" DROP DEFAULT;
                ALTER TABLE "SALES_ORDER_LINE" ALTER COLUMN "ProductPriceEffectiveFrom" DROP DEFAULT;
                ALTER TABLE "SALES_ORDER_LINE" ALTER COLUMN "PricedAt" DROP DEFAULT;
                ALTER TABLE "SALES_ORDER_LINE" ALTER COLUMN "MlorMinimumLifeRatio" DROP DEFAULT;
                ALTER TABLE "SALES_ORDER_LINE" ALTER COLUMN "MlorResolvedAt" DROP DEFAULT;
                ALTER TABLE "SALES_ORDER_LINE" ALTER COLUMN "MlorSourceScope" DROP DEFAULT;
                ALTER TABLE "SALES_ORDER_LINE" ALTER COLUMN "MlorSourceVersion" DROP DEFAULT;
                ALTER TABLE "ORDER_PROMOTION" ALTER COLUMN "PromotionCodeSnapshot" DROP DEFAULT;
                ALTER TABLE "ORDER_PROMOTION" ALTER COLUMN "PromotionNameSnapshot" DROP DEFAULT;
                ALTER TABLE "ORDER_PROMOTION" ALTER COLUMN "PromotionTypeSnapshot" DROP DEFAULT;
                ALTER TABLE "ORDER_PROMOTION" ALTER COLUMN "MinimumQuantitySnapshot" DROP DEFAULT;
                ALTER TABLE "ORDER_PROMOTION" ALTER COLUMN "EffectiveFromSnapshot" DROP DEFAULT;
                ALTER TABLE "ORDER_PROMOTION" ALTER COLUMN "EffectiveToSnapshot" DROP DEFAULT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_LINE_MlorProductCategoryId",
                table: "SALES_ORDER_LINE",
                column: "MlorProductCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_LINE_MlorSourceRuleId",
                table: "SALES_ORDER_LINE",
                column: "MlorSourceRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_LINE_ProductPriceId",
                table: "SALES_ORDER_LINE",
                column: "ProductPriceId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SALES_ORDER_LINE_MLOR_RATIO",
                table: "SALES_ORDER_LINE",
                sql: "\"MlorMinimumLifeRatio\" > 0 AND \"MlorMinimumLifeRatio\" < 1");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_ORDER_LINE_PRODUCT_CATEGORY_MlorProductCategoryId",
                table: "SALES_ORDER_LINE",
                column: "MlorProductCategoryId",
                principalTable: "PRODUCT_CATEGORY",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_ORDER_LINE_PRODUCT_PRICE_ProductPriceId",
                table: "SALES_ORDER_LINE",
                column: "ProductPriceId",
                principalTable: "PRODUCT_PRICE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_ORDER_LINE_RETAILER_PRODUCT_MLOR_MlorSourceRuleId",
                table: "SALES_ORDER_LINE",
                column: "MlorSourceRuleId",
                principalTable: "RETAILER_PRODUCT_MLOR",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_ORDER_LINE_PRODUCT_CATEGORY_MlorProductCategoryId",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropForeignKey(
                name: "FK_SALES_ORDER_LINE_PRODUCT_PRICE_ProductPriceId",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropForeignKey(
                name: "FK_SALES_ORDER_LINE_RETAILER_PRODUCT_MLOR_MlorSourceRuleId",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropIndex(
                name: "IX_SALES_ORDER_LINE_MlorProductCategoryId",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropIndex(
                name: "IX_SALES_ORDER_LINE_MlorSourceRuleId",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropIndex(
                name: "IX_SALES_ORDER_LINE_ProductPriceId",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SALES_ORDER_LINE_MLOR_RATIO",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "MlorMinimumLifeHours",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "MlorMinimumLifeRatio",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "MlorProductCategoryId",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "MlorResolvedAt",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "MlorSourceRuleId",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "MlorSourceScope",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "MlorSourceVersion",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "PricedAt",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "ProductPriceEffectiveFrom",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "ProductPriceEffectiveTo",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "ProductPriceId",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "EffectiveFromSnapshot",
                table: "ORDER_PROMOTION");

            migrationBuilder.DropColumn(
                name: "EffectiveToSnapshot",
                table: "ORDER_PROMOTION");

            migrationBuilder.DropColumn(
                name: "MinimumQuantitySnapshot",
                table: "ORDER_PROMOTION");

            migrationBuilder.DropColumn(
                name: "PromotionCodeSnapshot",
                table: "ORDER_PROMOTION");

            migrationBuilder.DropColumn(
                name: "PromotionNameSnapshot",
                table: "ORDER_PROMOTION");

            migrationBuilder.DropColumn(
                name: "PromotionTypeSnapshot",
                table: "ORDER_PROMOTION");
        }
    }
}
