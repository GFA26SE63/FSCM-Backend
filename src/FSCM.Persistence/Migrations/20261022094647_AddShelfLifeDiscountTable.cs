using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShelfLifeDiscountTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SHELF_LIFE_DISCOUNT_TABLE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHELF_LIFE_DISCOUNT_TABLE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SHELF_LIFE_DISCOUNT_TABLE_USER_ACCOUNT_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SHELF_LIFE_DISCOUNT_TIER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShelfLifeDiscountTableId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    MinRemainingPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    MaxRemainingPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    DiscountType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DiscountValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHELF_LIFE_DISCOUNT_TIER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SHELF_LIFE_DISCOUNT_TIER_PRODUCT_CATEGORY_ProductCategoryId",
                        column: x => x.ProductCategoryId,
                        principalTable: "PRODUCT_CATEGORY",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SHELF_LIFE_DISCOUNT_TIER_PRODUCT_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PRODUCT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SHELF_LIFE_DISCOUNT_TIER_SHELF_LIFE_DISCOUNT_TABLE_ShelfLif~",
                        column: x => x.ShelfLifeDiscountTableId,
                        principalTable: "SHELF_LIFE_DISCOUNT_TABLE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SHELF_LIFE_DISCOUNT_TABLE_CreatedByUserId",
                table: "SHELF_LIFE_DISCOUNT_TABLE",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SHELF_LIFE_DISCOUNT_TABLE_IsActive",
                table: "SHELF_LIFE_DISCOUNT_TABLE",
                column: "IsActive",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_SHELF_LIFE_DISCOUNT_TABLE_Version",
                table: "SHELF_LIFE_DISCOUNT_TABLE",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHELF_LIFE_DISCOUNT_TIER_ProductCategoryId",
                table: "SHELF_LIFE_DISCOUNT_TIER",
                column: "ProductCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_SHELF_LIFE_DISCOUNT_TIER_ProductId",
                table: "SHELF_LIFE_DISCOUNT_TIER",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SHELF_LIFE_DISCOUNT_TIER_ShelfLifeDiscountTableId_Scope_Pro~",
                table: "SHELF_LIFE_DISCOUNT_TIER",
                columns: new[] { "ShelfLifeDiscountTableId", "Scope", "ProductId", "MinRemainingPercent" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SHELF_LIFE_DISCOUNT_TIER");

            migrationBuilder.DropTable(
                name: "SHELF_LIFE_DISCOUNT_TABLE");
        }
    }
}
