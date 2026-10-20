using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddS1BCatalogTerritoryMaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EffectiveFrom",
                table: "TRANSIT_TIME_RULE",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EffectiveTo",
                table: "TRANSIT_TIME_RULE",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "TRANSIT_TIME_RULE",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RequestedCreditTermDays",
                table: "RETAILER",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NormalizedAt",
                table: "MAP_LOCATION",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "MAP_LOCATION",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "MAP_LOCATION",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE "TRANSIT_TIME_RULE"
                SET "Version" = 1, "EffectiveFrom" = "CreatedAt";

                UPDATE "MAP_LOCATION"
                SET "Provider" = 'Manual', "Version" = 1;

                UPDATE "RETAILER"
                SET "RequestedCreditTermDays" = "CreditTermDays",
                    "CreditTermDays" = CASE
                        WHEN "Status" IN ('Active', 'Inactive') THEN "CreditTermDays"
                        ELSE 0
                    END;

                UPDATE "RETAILER_PRODUCT_MLOR"
                SET "MinimumLifeRatio" = CASE
                    WHEN "MinimumLifeRatio" <= 0 THEN 0.0001
                    WHEN "MinimumLifeRatio" >= 1 THEN 0.9999
                    ELSE "MinimumLifeRatio"
                END;

                UPDATE "SYSTEM_CONFIGURATION"
                SET "IsActive" = FALSE, "UpdatedAt" = NOW()
                WHERE "ConfigKey" = 'replenishment.regional_minimum_stock_cartons'
                  AND "IsActive" = TRUE;
                """);

            migrationBuilder.CreateTable(
                name: "MAP_DISTANCE_CACHE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetailerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginLocationVersion = table.Column<int>(type: "integer", nullable: false),
                    DestinationLocationVersion = table.Column<int>(type: "integer", nullable: false),
                    DistanceMeters = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MAP_DISTANCE_CACHE", x => x.Id);
                    table.CheckConstraint("CK_MAP_DISTANCE_CACHE_Distance", "\"DistanceMeters\" > 0");
                    table.CheckConstraint("CK_MAP_DISTANCE_CACHE_Duration", "\"DurationSeconds\" > 0");
                    table.ForeignKey(
                        name: "FK_MAP_DISTANCE_CACHE_MAP_LOCATION_DestinationLocationId",
                        column: x => x.DestinationLocationId,
                        principalTable: "MAP_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MAP_DISTANCE_CACHE_MAP_LOCATION_OriginLocationId",
                        column: x => x.OriginLocationId,
                        principalTable: "MAP_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MAP_DISTANCE_CACHE_RETAILER_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "RETAILER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MAP_DISTANCE_CACHE_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TRANSIT_TIME_RULE_WarehouseId_Version",
                table: "TRANSIT_TIME_RULE",
                columns: new[] { "WarehouseId", "Version" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_RETAILER_PRODUCT_MLOR_Ratio",
                table: "RETAILER_PRODUCT_MLOR",
                sql: "\"MinimumLifeRatio\" > 0 AND \"MinimumLifeRatio\" < 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RETAILER_RequestedCreditTermDays",
                table: "RETAILER",
                sql: "\"RequestedCreditTermDays\" IS NULL OR \"RequestedCreditTermDays\" IN (0, 7, 14, 30)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MAP_LOCATION_Entity",
                table: "MAP_LOCATION",
                sql: "(\"WarehouseId\" IS NOT NULL) <> (\"RetailerId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_MAP_DISTANCE_CACHE_DestinationLocationId",
                table: "MAP_DISTANCE_CACHE",
                column: "DestinationLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_MAP_DISTANCE_CACHE_OriginLocationId",
                table: "MAP_DISTANCE_CACHE",
                column: "OriginLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_MAP_DISTANCE_CACHE_RetailerId",
                table: "MAP_DISTANCE_CACHE",
                column: "RetailerId");

            migrationBuilder.CreateIndex(
                name: "IX_MAP_DISTANCE_CACHE_WarehouseId_RetailerId",
                table: "MAP_DISTANCE_CACHE",
                columns: new[] { "WarehouseId", "RetailerId" },
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_MAP_DISTANCE_CACHE_WarehouseId_RetailerId_Version",
                table: "MAP_DISTANCE_CACHE",
                columns: new[] { "WarehouseId", "RetailerId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MAP_DISTANCE_CACHE");

            migrationBuilder.DropIndex(
                name: "IX_TRANSIT_TIME_RULE_WarehouseId_Version",
                table: "TRANSIT_TIME_RULE");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RETAILER_PRODUCT_MLOR_Ratio",
                table: "RETAILER_PRODUCT_MLOR");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RETAILER_RequestedCreditTermDays",
                table: "RETAILER");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MAP_LOCATION_Entity",
                table: "MAP_LOCATION");

            migrationBuilder.DropColumn(
                name: "EffectiveFrom",
                table: "TRANSIT_TIME_RULE");

            migrationBuilder.DropColumn(
                name: "EffectiveTo",
                table: "TRANSIT_TIME_RULE");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "TRANSIT_TIME_RULE");

            migrationBuilder.DropColumn(
                name: "RequestedCreditTermDays",
                table: "RETAILER");

            migrationBuilder.DropColumn(
                name: "NormalizedAt",
                table: "MAP_LOCATION");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "MAP_LOCATION");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "MAP_LOCATION");
        }
    }
}
