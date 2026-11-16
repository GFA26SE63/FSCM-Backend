using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFactoryReceiptSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProductName",
                table: "FACTORY_PRODUCTION_BATCH",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Sku",
                table: "FACTORY_PRODUCTION_BATCH",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "FACTORY_RECEIPT_SYNC",
                columns: table => new
                {
                    PalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FACTORY_RECEIPT_SYNC", x => x.PalletId);
                    table.ForeignKey(
                        name: "FK_FACTORY_RECEIPT_SYNC_FACTORY_PALLET_PalletId",
                        column: x => x.PalletId,
                        principalTable: "FACTORY_PALLET",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FACTORY_RECEIPT_SYNC_WAREHOUSE_DOCUMENT_ReceiptDocumentId",
                        column: x => x.ReceiptDocumentId,
                        principalTable: "WAREHOUSE_DOCUMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_RECEIPT_SYNC_ReceiptDocumentId",
                table: "FACTORY_RECEIPT_SYNC",
                column: "ReceiptDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_RECEIPT_SYNC_SyncedAt",
                table: "FACTORY_RECEIPT_SYNC",
                column: "SyncedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FACTORY_RECEIPT_SYNC");

            migrationBuilder.DropColumn(
                name: "ProductName",
                table: "FACTORY_PRODUCTION_BATCH");

            migrationBuilder.DropColumn(
                name: "Sku",
                table: "FACTORY_PRODUCTION_BATCH");
        }
    }
}
