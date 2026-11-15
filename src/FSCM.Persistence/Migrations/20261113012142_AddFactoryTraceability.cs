using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFactoryTraceability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BATCH_WarehouseId_ProductId_ProductionDate_ExpirationDate",
                table: "BATCH");

            migrationBuilder.AlterColumn<string>(
                name: "BatchCode",
                table: "BATCH",
                type: "character varying(240)",
                maxLength: 240,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AddColumn<Guid>(
                name: "FactoryProductionBatchId",
                table: "BATCH",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "WarehouseReceiptDate",
                table: "BATCH",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FACTORY_PRODUCTION_BATCH",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    FactoryCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    ProductionLine = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    DailySequence = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    UnitsPerCarton = table.Column<int>(type: "integer", nullable: false),
                    OperationKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FACTORY_PRODUCTION_BATCH", x => x.Id);
                    table.CheckConstraint("CK_FACTORY_BATCH_Count", "\"DailySequence\" BETWEEN 1 AND 9999 AND \"UnitsPerCarton\" > 0");
                    table.CheckConstraint("CK_FACTORY_BATCH_Dates", "\"ProductionDate\" < \"ExpirationDate\"");
                    table.CheckConstraint("CK_FACTORY_BATCH_Line", "\"ProductionLine\" IN ('A','B','C','D')");
                    table.ForeignKey(
                        name: "FK_FACTORY_PRODUCTION_BATCH_PRODUCT_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PRODUCT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FACTORY_PRODUCTION_BATCH_USER_ACCOUNT_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FACTORY_PALLET",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductionBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DestinationWarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceiptDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FACTORY_PALLET", x => x.Id);
                    table.CheckConstraint("CK_FACTORY_PALLET_Status", "\"Status\" IN ('Packed','Dispatched','Received')");
                    table.ForeignKey(
                        name: "FK_FACTORY_PALLET_FACTORY_PRODUCTION_BATCH_ProductionBatchId",
                        column: x => x.ProductionBatchId,
                        principalTable: "FACTORY_PRODUCTION_BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FACTORY_PALLET_WAREHOUSE_DOCUMENT_ReceiptDocumentId",
                        column: x => x.ReceiptDocumentId,
                        principalTable: "WAREHOUSE_DOCUMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FACTORY_PALLET_WAREHOUSE_DestinationWarehouseId",
                        column: x => x.DestinationWarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FACTORY_CARTON",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FACTORY_CARTON", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FACTORY_CARTON_FACTORY_PALLET_PalletId",
                        column: x => x.PalletId,
                        principalTable: "FACTORY_PALLET",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FACTORY_PRODUCT_UNIT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CartonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FACTORY_PRODUCT_UNIT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FACTORY_PRODUCT_UNIT_FACTORY_CARTON_CartonId",
                        column: x => x.CartonId,
                        principalTable: "FACTORY_CARTON",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_FactoryProductionBatchId",
                table: "BATCH",
                column: "FactoryProductionBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_WarehouseId_FactoryProductionBatchId_WarehouseReceipt~",
                table: "BATCH",
                columns: new[] { "WarehouseId", "FactoryProductionBatchId", "WarehouseReceiptDate" },
                unique: true,
                filter: "\"FactoryProductionBatchId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_WarehouseId_ProductId_ProductionDate_ExpirationDate",
                table: "BATCH",
                columns: new[] { "WarehouseId", "ProductId", "ProductionDate", "ExpirationDate" },
                unique: true,
                filter: "\"FactoryProductionBatchId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_CARTON_Code",
                table: "FACTORY_CARTON",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_CARTON_PalletId",
                table: "FACTORY_CARTON",
                column: "PalletId");

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_PALLET_Code",
                table: "FACTORY_PALLET",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_PALLET_DestinationWarehouseId",
                table: "FACTORY_PALLET",
                column: "DestinationWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_PALLET_ProductionBatchId",
                table: "FACTORY_PALLET",
                column: "ProductionBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_PALLET_ReceiptDocumentId",
                table: "FACTORY_PALLET",
                column: "ReceiptDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_PRODUCT_UNIT_CartonId",
                table: "FACTORY_PRODUCT_UNIT",
                column: "CartonId");

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_PRODUCT_UNIT_Code",
                table: "FACTORY_PRODUCT_UNIT",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_PRODUCTION_BATCH_Code",
                table: "FACTORY_PRODUCTION_BATCH",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_PRODUCTION_BATCH_CreatedByUserId",
                table: "FACTORY_PRODUCTION_BATCH",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_PRODUCTION_BATCH_FactoryCode_ProductionDate_DailySe~",
                table: "FACTORY_PRODUCTION_BATCH",
                columns: new[] { "FactoryCode", "ProductionDate", "DailySequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_PRODUCTION_BATCH_OperationKey",
                table: "FACTORY_PRODUCTION_BATCH",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_PRODUCTION_BATCH_ProductId",
                table: "FACTORY_PRODUCTION_BATCH",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_BATCH_FACTORY_PRODUCTION_BATCH_FactoryProductionBatchId",
                table: "BATCH",
                column: "FactoryProductionBatchId",
                principalTable: "FACTORY_PRODUCTION_BATCH",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BATCH_FACTORY_PRODUCTION_BATCH_FactoryProductionBatchId",
                table: "BATCH");

            migrationBuilder.DropTable(
                name: "FACTORY_PRODUCT_UNIT");

            migrationBuilder.DropTable(
                name: "FACTORY_CARTON");

            migrationBuilder.DropTable(
                name: "FACTORY_PALLET");

            migrationBuilder.DropTable(
                name: "FACTORY_PRODUCTION_BATCH");

            migrationBuilder.DropIndex(
                name: "IX_BATCH_FactoryProductionBatchId",
                table: "BATCH");

            migrationBuilder.DropIndex(
                name: "IX_BATCH_WarehouseId_FactoryProductionBatchId_WarehouseReceipt~",
                table: "BATCH");

            migrationBuilder.DropIndex(
                name: "IX_BATCH_WarehouseId_ProductId_ProductionDate_ExpirationDate",
                table: "BATCH");

            migrationBuilder.DropColumn(
                name: "FactoryProductionBatchId",
                table: "BATCH");

            migrationBuilder.DropColumn(
                name: "WarehouseReceiptDate",
                table: "BATCH");

            migrationBuilder.AlterColumn<string>(
                name: "BatchCode",
                table: "BATCH",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(240)",
                oldMaxLength: 240);

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_WarehouseId_ProductId_ProductionDate_ExpirationDate",
                table: "BATCH",
                columns: new[] { "WarehouseId", "ProductId", "ProductionDate", "ExpirationDate" },
                unique: true);
        }
    }
}
