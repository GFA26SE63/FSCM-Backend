using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedTransitTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TRANSIT_TABLE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    MaxServingDistanceMeters = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_TRANSIT_TABLE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TRANSIT_TABLE_USER_ACCOUNT_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TRANSIT_TABLE_BAND",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransitTableId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinDistanceMeters = table.Column<int>(type: "integer", nullable: false),
                    MaxDistanceMeters = table.Column<int>(type: "integer", nullable: false),
                    HandlingMinutes = table.Column<int>(type: "integer", nullable: false),
                    TransitMinutes = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCostAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSIT_TABLE_BAND", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TRANSIT_TABLE_BAND_TRANSIT_TABLE_TransitTableId",
                        column: x => x.TransitTableId,
                        principalTable: "TRANSIT_TABLE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TRANSIT_TABLE_CreatedByUserId",
                table: "TRANSIT_TABLE",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSIT_TABLE_IsActive",
                table: "TRANSIT_TABLE",
                column: "IsActive",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSIT_TABLE_Version",
                table: "TRANSIT_TABLE",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRANSIT_TABLE_BAND_TransitTableId_MinDistanceMeters",
                table: "TRANSIT_TABLE_BAND",
                columns: new[] { "TransitTableId", "MinDistanceMeters" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TRANSIT_TABLE_BAND");

            migrationBuilder.DropTable(
                name: "TRANSIT_TABLE");
        }
    }
}
