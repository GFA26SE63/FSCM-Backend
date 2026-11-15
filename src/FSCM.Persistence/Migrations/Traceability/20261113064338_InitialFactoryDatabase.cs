using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations.Traceability
{
    /// <inheritdoc />
    public partial class InitialFactoryDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    Sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
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
                name: "IX_FACTORY_PALLET_ProductionBatchId",
                table: "FACTORY_PALLET",
                column: "ProductionBatchId");

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
                name: "IX_FACTORY_PRODUCTION_BATCH_FactoryCode_ProductionDate_DailySe~",
                table: "FACTORY_PRODUCTION_BATCH",
                columns: new[] { "FactoryCode", "ProductionDate", "DailySequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FACTORY_PRODUCTION_BATCH_OperationKey",
                table: "FACTORY_PRODUCTION_BATCH",
                column: "OperationKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FACTORY_PRODUCT_UNIT");

            migrationBuilder.DropTable(
                name: "FACTORY_CARTON");

            migrationBuilder.DropTable(
                name: "FACTORY_PALLET");

            migrationBuilder.DropTable(
                name: "FACTORY_PRODUCTION_BATCH");
        }
    }
}
