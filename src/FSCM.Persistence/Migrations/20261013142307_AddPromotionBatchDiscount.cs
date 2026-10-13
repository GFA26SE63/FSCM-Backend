using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionBatchDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PROMOTION_RETAILER_PromotionId",
                table: "PROMOTION_RETAILER");

            migrationBuilder.DropIndex(
                name: "IX_PROMOTION_PRODUCT_PromotionId",
                table: "PROMOTION_PRODUCT");

            migrationBuilder.DropIndex(
                name: "IX_PROMOTION_CATEGORY_PromotionId",
                table: "PROMOTION_CATEGORY");

            migrationBuilder.DropIndex(
                name: "IX_BATCH_PROMOTION_BatchId",
                table: "BATCH_PROMOTION");

            migrationBuilder.CreateSequence(
                name: "PromotionCodeSequence",
                startValue: 4L);

            migrationBuilder.CreateIndex(
                name: "IX_PROMOTION_RETAILER_PromotionId_RetailerId",
                table: "PROMOTION_RETAILER",
                columns: new[] { "PromotionId", "RetailerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PROMOTION_PRODUCT_PromotionId_ProductId",
                table: "PROMOTION_PRODUCT",
                columns: new[] { "PromotionId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PROMOTION_CATEGORY_PromotionId_ProductCategoryId",
                table: "PROMOTION_CATEGORY",
                columns: new[] { "PromotionId", "ProductCategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_PROMOTION_BatchId_PromotionId",
                table: "BATCH_PROMOTION",
                columns: new[] { "BatchId", "PromotionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PROMOTION_RETAILER_PromotionId_RetailerId",
                table: "PROMOTION_RETAILER");

            migrationBuilder.DropIndex(
                name: "IX_PROMOTION_PRODUCT_PromotionId_ProductId",
                table: "PROMOTION_PRODUCT");

            migrationBuilder.DropIndex(
                name: "IX_PROMOTION_CATEGORY_PromotionId_ProductCategoryId",
                table: "PROMOTION_CATEGORY");

            migrationBuilder.DropIndex(
                name: "IX_BATCH_PROMOTION_BatchId_PromotionId",
                table: "BATCH_PROMOTION");

            migrationBuilder.DropSequence(
                name: "PromotionCodeSequence");

            migrationBuilder.CreateIndex(
                name: "IX_PROMOTION_RETAILER_PromotionId",
                table: "PROMOTION_RETAILER",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_PROMOTION_PRODUCT_PromotionId",
                table: "PROMOTION_PRODUCT",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_PROMOTION_CATEGORY_PromotionId",
                table: "PROMOTION_CATEGORY",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_PROMOTION_BatchId",
                table: "BATCH_PROMOTION",
                column: "BatchId");
        }
    }
}
