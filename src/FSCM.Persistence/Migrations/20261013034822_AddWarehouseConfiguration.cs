using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TRANSIT_TIME_RULE_WarehouseId",
                table: "TRANSIT_TIME_RULE");

            migrationBuilder.DropIndex(
                name: "IX_SYSTEM_CONFIGURATION_ConfigKey",
                table: "SYSTEM_CONFIGURATION");

            migrationBuilder.DropIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_RetailerId",
                table: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "WAREHOUSE",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EffectiveFrom",
                table: "SYSTEM_CONFIGURATION",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "SYSTEM_CONFIGURATION",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"WAREHOUSE\" SET \"Type\" = 'Central' WHERE \"Type\" IS NULL;");
            migrationBuilder.Sql("UPDATE \"SYSTEM_CONFIGURATION\" SET \"EffectiveFrom\" = \"CreatedAt\", \"Version\" = 1 WHERE \"EffectiveFrom\" IS NULL OR \"Version\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "WAREHOUSE",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EffectiveFrom",
                table: "SYSTEM_CONFIGURATION",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Version",
                table: "SYSTEM_CONFIGURATION",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRANSIT_TIME_RULE_WarehouseId_MinDistanceMeters_MaxDistance~",
                table: "TRANSIT_TIME_RULE",
                columns: new[] { "WarehouseId", "MinDistanceMeters", "MaxDistanceMeters", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_CONFIGURATION_ConfigKey",
                table: "SYSTEM_CONFIGURATION",
                column: "ConfigKey",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_CONFIGURATION_ConfigKey_Version",
                table: "SYSTEM_CONFIGURATION",
                columns: new[] { "ConfigKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_RetailerId_ProductId",
                table: "RETAILER_PRODUCT_MLOR",
                columns: new[] { "RetailerId", "ProductId" },
                unique: true,
                filter: "\"IsActive\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TRANSIT_TIME_RULE_WarehouseId_MinDistanceMeters_MaxDistance~",
                table: "TRANSIT_TIME_RULE");

            migrationBuilder.DropIndex(
                name: "IX_SYSTEM_CONFIGURATION_ConfigKey",
                table: "SYSTEM_CONFIGURATION");

            migrationBuilder.DropIndex(
                name: "IX_SYSTEM_CONFIGURATION_ConfigKey_Version",
                table: "SYSTEM_CONFIGURATION");

            migrationBuilder.DropIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_RetailerId_ProductId",
                table: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "WAREHOUSE");

            migrationBuilder.DropColumn(
                name: "EffectiveFrom",
                table: "SYSTEM_CONFIGURATION");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "SYSTEM_CONFIGURATION");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSIT_TIME_RULE_WarehouseId",
                table: "TRANSIT_TIME_RULE",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_CONFIGURATION_ConfigKey",
                table: "SYSTEM_CONFIGURATION",
                column: "ConfigKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_PRODUCT_MLOR_RetailerId",
                table: "RETAILER_PRODUCT_MLOR",
                column: "RetailerId");
        }
    }
}
