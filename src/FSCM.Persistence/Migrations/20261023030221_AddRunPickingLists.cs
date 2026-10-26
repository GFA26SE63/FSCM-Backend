using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunPickingLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RUN_PICKING_LIST",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PickingCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AllocationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationPlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RUN_PICKING_LIST", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_LIST_ALLOCATION_PLAN_VERSION_AllocationPlanVers~",
                        column: x => x.AllocationPlanVersionId,
                        principalTable: "ALLOCATION_PLAN_VERSION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_LIST_ALLOCATION_RUN_AllocationRunId",
                        column: x => x.AllocationRunId,
                        principalTable: "ALLOCATION_RUN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_LIST_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RUN_PICKING_GATHER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunPickingListId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkuSnapshot = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchCodeSnapshot = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequiredQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RUN_PICKING_GATHER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_GATHER_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_GATHER_RUN_PICKING_LIST_RunPickingListId",
                        column: x => x.RunPickingListId,
                        principalTable: "RUN_PICKING_LIST",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RUN_PICKING_SPLIT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunPickingGatherId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRunAllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRunLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationPlanPriceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderCodeSnapshot = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RUN_PICKING_SPLIT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_SPLIT_ALLOCATION_PLAN_PRICE_AllocationPlanPrice~",
                        column: x => x.AllocationPlanPriceId,
                        principalTable: "ALLOCATION_PLAN_PRICE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_SPLIT_ALLOCATION_RUN_ALLOCATION_AllocationRunAl~",
                        column: x => x.AllocationRunAllocationId,
                        principalTable: "ALLOCATION_RUN_ALLOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_SPLIT_RUN_PICKING_GATHER_RunPickingGatherId",
                        column: x => x.RunPickingGatherId,
                        principalTable: "RUN_PICKING_GATHER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_GATHER_BatchId",
                table: "RUN_PICKING_GATHER",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_GATHER_RunPickingListId_BatchId",
                table: "RUN_PICKING_GATHER",
                columns: new[] { "RunPickingListId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_LIST_AllocationPlanVersionId",
                table: "RUN_PICKING_LIST",
                column: "AllocationPlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_LIST_AllocationRunId_WarehouseId",
                table: "RUN_PICKING_LIST",
                columns: new[] { "AllocationRunId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_LIST_PickingCode",
                table: "RUN_PICKING_LIST",
                column: "PickingCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_LIST_WarehouseId",
                table: "RUN_PICKING_LIST",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_SPLIT_AllocationPlanPriceId",
                table: "RUN_PICKING_SPLIT",
                column: "AllocationPlanPriceId");

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_SPLIT_AllocationRunAllocationId",
                table: "RUN_PICKING_SPLIT",
                column: "AllocationRunAllocationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_SPLIT_RunPickingGatherId",
                table: "RUN_PICKING_SPLIT",
                column: "RunPickingGatherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RUN_PICKING_SPLIT");

            migrationBuilder.DropTable(
                name: "RUN_PICKING_GATHER");

            migrationBuilder.DropTable(
                name: "RUN_PICKING_LIST");
        }
    }
}
