using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrototypePromotionRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                table: "PROMOTION_PRODUCT",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumQuantity",
                table: "PROMOTION_PRODUCT",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CampaignMode",
                table: "PROMOTION",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "ProductGroup");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "PROMOTION_PRODUCT");

            migrationBuilder.DropColumn(
                name: "MinimumQuantity",
                table: "PROMOTION_PRODUCT");

            migrationBuilder.DropColumn(
                name: "CampaignMode",
                table: "PROMOTION");
        }
    }
}
