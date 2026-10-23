using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssignedAt",
                table: "SHIPMENT",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverCodeSnapshot",
                table: "SHIPMENT",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DriverId",
                table: "SHIPMENT",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverLicenseSnapshot",
                table: "SHIPMENT",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverNameSnapshot",
                table: "SHIPMENT",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverPhoneSnapshot",
                table: "SHIPMENT",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlateNumberSnapshot",
                table: "SHIPMENT",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VehicleId",
                table: "SHIPMENT",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleTypeSnapshot",
                table: "SHIPMENT",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_DriverId",
                table: "SHIPMENT",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_VehicleId",
                table: "SHIPMENT",
                column: "VehicleId");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_DRIVER_DriverId",
                table: "SHIPMENT",
                column: "DriverId",
                principalTable: "DRIVER",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_VEHICLE_VehicleId",
                table: "SHIPMENT",
                column: "VehicleId",
                principalTable: "VEHICLE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_DRIVER_DriverId",
                table: "SHIPMENT");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_VEHICLE_VehicleId",
                table: "SHIPMENT");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_DriverId",
                table: "SHIPMENT");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_VehicleId",
                table: "SHIPMENT");

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "SHIPMENT");

            migrationBuilder.DropColumn(
                name: "DriverCodeSnapshot",
                table: "SHIPMENT");

            migrationBuilder.DropColumn(
                name: "DriverId",
                table: "SHIPMENT");

            migrationBuilder.DropColumn(
                name: "DriverLicenseSnapshot",
                table: "SHIPMENT");

            migrationBuilder.DropColumn(
                name: "DriverNameSnapshot",
                table: "SHIPMENT");

            migrationBuilder.DropColumn(
                name: "DriverPhoneSnapshot",
                table: "SHIPMENT");

            migrationBuilder.DropColumn(
                name: "PlateNumberSnapshot",
                table: "SHIPMENT");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "SHIPMENT");

            migrationBuilder.DropColumn(
                name: "VehicleTypeSnapshot",
                table: "SHIPMENT");
        }
    }
}
