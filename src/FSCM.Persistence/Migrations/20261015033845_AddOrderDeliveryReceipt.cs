using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderDeliveryReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DELIVERY_SalesOrderId",
                table: "DELIVERY");

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "VEHICLE",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "OperationKey",
                table: "RECEIPT_CONFIRMATION",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "RECEIPT_CONFIRMATION",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "DRIVER",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "DRIVER",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "DriverCodeSnapshot",
                table: "DELIVERY",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DriverLicenseSnapshot",
                table: "DELIVERY",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DriverNameSnapshot",
                table: "DELIVERY",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DriverPhoneSnapshot",
                table: "DELIVERY",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OperationKey",
                table: "DELIVERY",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PlateNumberSnapshot",
                table: "DELIVERY",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VehicleTypeSnapshot",
                table: "DELIVERY",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "DELIVERY",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id", ROW_NUMBER() OVER (ORDER BY "CreatedAt", "Id") AS rn
                    FROM "DRIVER"
                )
                UPDATE "DRIVER" AS driver
                SET "Code" = 'NG-' || LPAD((900 + ranked.rn)::text, 3, '0'),
                    "WarehouseId" = (SELECT "Id" FROM "WAREHOUSE" ORDER BY "Code" LIMIT 1)
                FROM ranked
                WHERE driver."Id" = ranked."Id" AND driver."Code" = '';

                UPDATE "VEHICLE"
                SET "WarehouseId" = (SELECT "Id" FROM "WAREHOUSE" ORDER BY "Code" LIMIT 1)
                WHERE "WarehouseId" = '00000000-0000-0000-0000-000000000000';

                UPDATE "DELIVERY" AS delivery
                SET "OperationKey" = 'legacy-dispatch-' || REPLACE(delivery."Id"::text, '-', ''),
                    "DriverCodeSnapshot" = driver."Code",
                    "DriverNameSnapshot" = driver."FullName",
                    "DriverPhoneSnapshot" = driver."Phone",
                    "DriverLicenseSnapshot" = driver."LicenseNumber",
                    "PlateNumberSnapshot" = vehicle."PlateNumber",
                    "VehicleTypeSnapshot" = vehicle."VehicleType"
                FROM "DRIVER" AS driver, "VEHICLE" AS vehicle
                WHERE delivery."DriverId" = driver."Id" AND delivery."VehicleId" = vehicle."Id"
                  AND delivery."OperationKey" = '';

                UPDATE "RECEIPT_CONFIRMATION"
                SET "OperationKey" = 'legacy-receipt-' || REPLACE("Id"::text, '-', ''),
                    "RequestHash" = REPEAT('0', 64)
                WHERE "OperationKey" = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_VEHICLE_WarehouseId",
                table: "VEHICLE",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_CONFIRMATION_OperationKey",
                table: "RECEIPT_CONFIRMATION",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DRIVER_Code",
                table: "DRIVER",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DRIVER_WarehouseId",
                table: "DRIVER",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_DELIVERY_OperationKey",
                table: "DELIVERY",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DELIVERY_SalesOrderId",
                table: "DELIVERY",
                column: "SalesOrderId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DRIVER_WAREHOUSE_WarehouseId",
                table: "DRIVER",
                column: "WarehouseId",
                principalTable: "WAREHOUSE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VEHICLE_WAREHOUSE_WarehouseId",
                table: "VEHICLE",
                column: "WarehouseId",
                principalTable: "WAREHOUSE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DRIVER_WAREHOUSE_WarehouseId",
                table: "DRIVER");

            migrationBuilder.DropForeignKey(
                name: "FK_VEHICLE_WAREHOUSE_WarehouseId",
                table: "VEHICLE");

            migrationBuilder.DropIndex(
                name: "IX_VEHICLE_WarehouseId",
                table: "VEHICLE");

            migrationBuilder.DropIndex(
                name: "IX_RECEIPT_CONFIRMATION_OperationKey",
                table: "RECEIPT_CONFIRMATION");

            migrationBuilder.DropIndex(
                name: "IX_DRIVER_Code",
                table: "DRIVER");

            migrationBuilder.DropIndex(
                name: "IX_DRIVER_WarehouseId",
                table: "DRIVER");

            migrationBuilder.DropIndex(
                name: "IX_DELIVERY_OperationKey",
                table: "DELIVERY");

            migrationBuilder.DropIndex(
                name: "IX_DELIVERY_SalesOrderId",
                table: "DELIVERY");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "VEHICLE");

            migrationBuilder.DropColumn(
                name: "OperationKey",
                table: "RECEIPT_CONFIRMATION");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "RECEIPT_CONFIRMATION");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "DRIVER");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "DRIVER");

            migrationBuilder.DropColumn(
                name: "DriverCodeSnapshot",
                table: "DELIVERY");

            migrationBuilder.DropColumn(
                name: "DriverLicenseSnapshot",
                table: "DELIVERY");

            migrationBuilder.DropColumn(
                name: "DriverNameSnapshot",
                table: "DELIVERY");

            migrationBuilder.DropColumn(
                name: "DriverPhoneSnapshot",
                table: "DELIVERY");

            migrationBuilder.DropColumn(
                name: "OperationKey",
                table: "DELIVERY");

            migrationBuilder.DropColumn(
                name: "PlateNumberSnapshot",
                table: "DELIVERY");

            migrationBuilder.DropColumn(
                name: "VehicleTypeSnapshot",
                table: "DELIVERY");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "DELIVERY");

            migrationBuilder.CreateIndex(
                name: "IX_DELIVERY_SalesOrderId",
                table: "DELIVERY",
                column: "SalesOrderId");
        }
    }
}
