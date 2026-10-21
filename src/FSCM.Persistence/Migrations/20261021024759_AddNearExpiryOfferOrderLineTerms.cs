using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNearExpiryOfferOrderLineTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AcceptedAt",
                table: "SALES_ORDER_LINE",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AcceptedByUserId",
                table: "SALES_ORDER_LINE",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AcceptedNetUnitPrice",
                table: "SALES_ORDER_LINE",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultMlorRatio",
                table: "SALES_ORDER_LINE",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EffectiveMlorRatio",
                table: "SALES_ORDER_LINE",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0.25m);

            migrationBuilder.AddColumn<string>(
                name: "LineType",
                table: "SALES_ORDER_LINE",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "MlorSource",
                table: "SALES_ORDER_LINE",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "ProductDefault");

            migrationBuilder.AddColumn<Guid>(
                name: "NearExpiryOfferId",
                table: "SALES_ORDER_LINE",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NearExpiryOfferVersion",
                table: "SALES_ORDER_LINE",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_LINE_AcceptedByUserId",
                table: "SALES_ORDER_LINE",
                column: "AcceptedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_LINE_SalesOrderId_ProductId",
                table: "SALES_ORDER_LINE",
                columns: new[] { "SalesOrderId", "ProductId" },
                unique: true,
                filter: "\"LineType\" = 'Standard'");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_LINE_SalesOrderId_ProductId_NearExpiryOfferId_N~",
                table: "SALES_ORDER_LINE",
                columns: new[] { "SalesOrderId", "ProductId", "NearExpiryOfferId", "NearExpiryOfferVersion" },
                unique: true,
                filter: "\"LineType\" = 'NearExpiryOffer'");

            // The defaults above only backfill lines created before this contract existed. Drop them
            // so every new line must state its type, effective ratio, and MLOR source explicitly.
            migrationBuilder.Sql(
                """
                ALTER TABLE "SALES_ORDER_LINE"
                    ALTER COLUMN "LineType" DROP DEFAULT,
                    ALTER COLUMN "MlorSource" DROP DEFAULT,
                    ALTER COLUMN "EffectiveMlorRatio" DROP DEFAULT;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_ORDER_LINE_USER_ACCOUNT_AcceptedByUserId",
                table: "SALES_ORDER_LINE",
                column: "AcceptedByUserId",
                principalTable: "USER_ACCOUNT",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_ORDER_LINE_USER_ACCOUNT_AcceptedByUserId",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropIndex(
                name: "IX_SALES_ORDER_LINE_AcceptedByUserId",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropIndex(
                name: "IX_SALES_ORDER_LINE_SalesOrderId_ProductId",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropIndex(
                name: "IX_SALES_ORDER_LINE_SalesOrderId_ProductId_NearExpiryOfferId_N~",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "AcceptedAt",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "AcceptedByUserId",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "AcceptedNetUnitPrice",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "DefaultMlorRatio",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "EffectiveMlorRatio",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "LineType",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "MlorSource",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "NearExpiryOfferId",
                table: "SALES_ORDER_LINE");

            migrationBuilder.DropColumn(
                name: "NearExpiryOfferVersion",
                table: "SALES_ORDER_LINE");
        }
    }
}
