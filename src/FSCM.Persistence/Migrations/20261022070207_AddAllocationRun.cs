using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAllocationRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ALLOCATION_RUN",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CutoffAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AlgorithmSettings = table.Column<string>(type: "jsonb", nullable: false),
                    TransitTableVersion = table.Column<int>(type: "integer", nullable: false),
                    EtaScheduleVersion = table.Column<int>(type: "integer", nullable: false),
                    OrderCount = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SnapshotCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ALLOCATION_RUN", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ALLOCATION_RUN_ORDER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderCodeSnapshot = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RetailerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeliveryDeadline = table.Column<DateOnly>(type: "date", nullable: true),
                    PriorityRank = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ShortageReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ALLOCATION_RUN_ORDER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_RUN_ORDER_ALLOCATION_RUN_AllocationRunId",
                        column: x => x.AllocationRunId,
                        principalTable: "ALLOCATION_RUN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_RUN_ORDER_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ALLOCATION_RUN_STOCK",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SnapshotQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    RemainingQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    InventoryVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ALLOCATION_RUN_STOCK", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_RUN_STOCK_ALLOCATION_RUN_AllocationRunId",
                        column: x => x.AllocationRunId,
                        principalTable: "ALLOCATION_RUN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_RUN_STOCK_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ALLOCATION_RUN_LINE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRunOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkuSnapshot = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    TotalShelfLifeHours = table.Column<int>(type: "integer", nullable: false),
                    EffectiveMlorRatio = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    MlorSource = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    BaseNetUnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AllocatedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ALLOCATION_RUN_LINE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_RUN_LINE_ALLOCATION_RUN_ORDER_AllocationRunOrder~",
                        column: x => x.AllocationRunOrderId,
                        principalTable: "ALLOCATION_RUN_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_RUN_LINE_SALES_ORDER_LINE_SalesOrderLineId",
                        column: x => x.SalesOrderLineId,
                        principalTable: "SALES_ORDER_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_BusinessDate",
                table: "ALLOCATION_RUN",
                column: "BusinessDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_RunCode",
                table: "ALLOCATION_RUN",
                column: "RunCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_LINE_AllocationRunOrderId_SalesOrderLineId",
                table: "ALLOCATION_RUN_LINE",
                columns: new[] { "AllocationRunOrderId", "SalesOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_LINE_SalesOrderLineId",
                table: "ALLOCATION_RUN_LINE",
                column: "SalesOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_ORDER_AllocationRunId_PriorityRank",
                table: "ALLOCATION_RUN_ORDER",
                columns: new[] { "AllocationRunId", "PriorityRank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_ORDER_AllocationRunId_SalesOrderId",
                table: "ALLOCATION_RUN_ORDER",
                columns: new[] { "AllocationRunId", "SalesOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_ORDER_SalesOrderId",
                table: "ALLOCATION_RUN_ORDER",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_STOCK_AllocationRunId_BatchId",
                table: "ALLOCATION_RUN_STOCK",
                columns: new[] { "AllocationRunId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_STOCK_AllocationRunId_ProductId",
                table: "ALLOCATION_RUN_STOCK",
                columns: new[] { "AllocationRunId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_STOCK_BatchId",
                table: "ALLOCATION_RUN_STOCK",
                column: "BatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ALLOCATION_RUN_LINE");

            migrationBuilder.DropTable(
                name: "ALLOCATION_RUN_STOCK");

            migrationBuilder.DropTable(
                name: "ALLOCATION_RUN_ORDER");

            migrationBuilder.DropTable(
                name: "ALLOCATION_RUN");
        }
    }
}
