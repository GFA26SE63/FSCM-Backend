using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogProductLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PRODUCT_PRICE_ProductId",
                table: "PRODUCT_PRICE");

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesProductId",
                table: "PRODUCT",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PRODUCT_PRICE_ProductId",
                table: "PRODUCT_PRICE",
                column: "ProductId",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PRODUCT_PRICE_EffectiveRange",
                table: "PRODUCT_PRICE",
                sql: "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PRODUCT_PRICE_UnitPrice",
                table: "PRODUCT_PRICE",
                sql: "\"UnitPrice\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_PRODUCT_ReplacesProductId",
                table: "PRODUCT",
                column: "ReplacesProductId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PRODUCT_NearDateThresholdHours",
                table: "PRODUCT",
                sql: "\"NearDateThresholdHours\" >= 0 AND \"NearDateThresholdHours\" < \"ShelfLifeHours\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PRODUCT_Replacement",
                table: "PRODUCT",
                sql: "\"ReplacesProductId\" IS NULL OR \"ReplacesProductId\" <> \"Id\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PRODUCT_ShelfLifeHours",
                table: "PRODUCT",
                sql: "\"ShelfLifeHours\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PRODUCT_Status",
                table: "PRODUCT",
                sql: "\"Status\" IN ('Draft', 'Published', 'Inactive')");

            migrationBuilder.AddForeignKey(
                name: "FK_PRODUCT_PRODUCT_ReplacesProductId",
                table: "PRODUCT",
                column: "ReplacesProductId",
                principalTable: "PRODUCT",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PRODUCT_PRODUCT_ReplacesProductId",
                table: "PRODUCT");

            migrationBuilder.DropIndex(
                name: "IX_PRODUCT_PRICE_ProductId",
                table: "PRODUCT_PRICE");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PRODUCT_PRICE_EffectiveRange",
                table: "PRODUCT_PRICE");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PRODUCT_PRICE_UnitPrice",
                table: "PRODUCT_PRICE");

            migrationBuilder.DropIndex(
                name: "IX_PRODUCT_ReplacesProductId",
                table: "PRODUCT");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PRODUCT_NearDateThresholdHours",
                table: "PRODUCT");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PRODUCT_Replacement",
                table: "PRODUCT");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PRODUCT_ShelfLifeHours",
                table: "PRODUCT");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PRODUCT_Status",
                table: "PRODUCT");

            migrationBuilder.DropColumn(
                name: "ReplacesProductId",
                table: "PRODUCT");

            migrationBuilder.CreateIndex(
                name: "IX_PRODUCT_PRICE_ProductId",
                table: "PRODUCT_PRICE",
                column: "ProductId");
        }
    }
}
