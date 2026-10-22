using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteS1BWarehouseRetailerMlor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_RetailerId_ProductId",
                table: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.AddColumn<int>(
                name: "MinimumStockThresholdCartons",
                table: "WAREHOUSE",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedTransportCost",
                table: "TRANSIT_TIME_RULE",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "RETAILER_PRODUCT_MLOR",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "ProductCategoryId",
                table: "RETAILER_PRODUCT_MLOR",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "RETAILER_PRODUCT_MLOR",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "RETAILER_PRODUCT_MLOR",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql("""
                UPDATE "RETAILER_PRODUCT_MLOR" AS m
                SET "UpdatedByUserId" = r."DeclaredByUserId",
                    "Version" = CASE WHEN m."Version" < 1 THEN 1 ELSE m."Version" END
                FROM "RETAILER" AS r
                WHERE r."Id" = m."RetailerId";
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "UpdatedByUserId",
                table: "RETAILER_PRODUCT_MLOR",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WAREHOUSE_MinimumStockThresholdCartons",
                table: "WAREHOUSE",
                sql: "\"MinimumStockThresholdCartons\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TRANSIT_TIME_RULE_EstimatedTransportCost",
                table: "TRANSIT_TIME_RULE",
                sql: "\"EstimatedTransportCost\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_ProductCategoryId",
                table: "RETAILER_PRODUCT_MLOR",
                column: "ProductCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_RetailerId_ProductCategoryId",
                table: "RETAILER_PRODUCT_MLOR",
                columns: new[] { "RetailerId", "ProductCategoryId" },
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"ProductCategoryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_RetailerId_ProductId",
                table: "RETAILER_PRODUCT_MLOR",
                columns: new[] { "RetailerId", "ProductId" },
                unique: true,
                filter: "\"IsActive\" = TRUE AND \"ProductId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_RetailerId_Version",
                table: "RETAILER_PRODUCT_MLOR",
                columns: new[] { "RetailerId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_UpdatedByUserId",
                table: "RETAILER_PRODUCT_MLOR",
                column: "UpdatedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RETAILER_PRODUCT_MLOR_Scope",
                table: "RETAILER_PRODUCT_MLOR",
                sql: "(\"ProductCategoryId\" IS NOT NULL) <> (\"ProductId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_RETAILER_PRODUCT_MLOR_PRODUCT_CATEGORY_ProductCategoryId",
                table: "RETAILER_PRODUCT_MLOR",
                column: "ProductCategoryId",
                principalTable: "PRODUCT_CATEGORY",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RETAILER_PRODUCT_MLOR_USER_ACCOUNT_UpdatedByUserId",
                table: "RETAILER_PRODUCT_MLOR",
                column: "UpdatedByUserId",
                principalTable: "USER_ACCOUNT",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RETAILER_PRODUCT_MLOR_PRODUCT_CATEGORY_ProductCategoryId",
                table: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.DropForeignKey(
                name: "FK_RETAILER_PRODUCT_MLOR_USER_ACCOUNT_UpdatedByUserId",
                table: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WAREHOUSE_MinimumStockThresholdCartons",
                table: "WAREHOUSE");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TRANSIT_TIME_RULE_EstimatedTransportCost",
                table: "TRANSIT_TIME_RULE");

            migrationBuilder.DropIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_ProductCategoryId",
                table: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.DropIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_RetailerId_ProductCategoryId",
                table: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.DropIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_RetailerId_ProductId",
                table: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.DropIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_RetailerId_Version",
                table: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.DropIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_UpdatedByUserId",
                table: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RETAILER_PRODUCT_MLOR_Scope",
                table: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.DropColumn(
                name: "MinimumStockThresholdCartons",
                table: "WAREHOUSE");

            migrationBuilder.DropColumn(
                name: "EstimatedTransportCost",
                table: "TRANSIT_TIME_RULE");

            migrationBuilder.DropColumn(
                name: "ProductCategoryId",
                table: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                table: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "RETAILER_PRODUCT_MLOR",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_RetailerId_ProductId",
                table: "RETAILER_PRODUCT_MLOR",
                columns: new[] { "RetailerId", "ProductId" },
                unique: true,
                filter: "\"IsActive\" = TRUE");
        }
    }
}
