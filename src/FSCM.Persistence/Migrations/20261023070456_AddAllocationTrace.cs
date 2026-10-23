using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAllocationTrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ALLOCATION_LINE_TRACE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationPlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRunLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    DrawnQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ShortageQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    RequiredShelfLifeHours = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ShelfLifeWeight = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    DistanceWeight = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    CoverageWeight = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    MaximumDistanceMeters = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ALLOCATION_LINE_TRACE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_LINE_TRACE_ALLOCATION_PLAN_VERSION_AllocationPla~",
                        column: x => x.AllocationPlanVersionId,
                        principalTable: "ALLOCATION_PLAN_VERSION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_LINE_TRACE_ALLOCATION_RUN_LINE_AllocationRunLine~",
                        column: x => x.AllocationRunLineId,
                        principalTable: "ALLOCATION_RUN_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ALLOCATION_TRACE_CANDIDATE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationLineTraceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EstimatedArrivalAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemainingShelfLifeHours = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    RequiredShelfLifeHours = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ALLOCATION_TRACE_CANDIDATE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_TRACE_CANDIDATE_ALLOCATION_LINE_TRACE_Allocation~",
                        column: x => x.AllocationLineTraceId,
                        principalTable: "ALLOCATION_LINE_TRACE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ALLOCATION_TRACE_SCORE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationLineTraceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Iteration = table.Column<int>(type: "integer", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseCodeSnapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RemainingDemand = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ShelfLifeScore = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    DistanceScore = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    CoverageScore = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    WeightedScore = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    Selected = table.Column<bool>(type: "boolean", nullable: false),
                    SelectedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ALLOCATION_TRACE_SCORE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_TRACE_SCORE_ALLOCATION_LINE_TRACE_AllocationLine~",
                        column: x => x.AllocationLineTraceId,
                        principalTable: "ALLOCATION_LINE_TRACE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_LINE_TRACE_AllocationPlanVersionId_AllocationRun~",
                table: "ALLOCATION_LINE_TRACE",
                columns: new[] { "AllocationPlanVersionId", "AllocationRunLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_LINE_TRACE_AllocationRunLineId",
                table: "ALLOCATION_LINE_TRACE",
                column: "AllocationRunLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_TRACE_CANDIDATE_AllocationLineTraceId_Stage_Batc~",
                table: "ALLOCATION_TRACE_CANDIDATE",
                columns: new[] { "AllocationLineTraceId", "Stage", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_TRACE_SCORE_AllocationLineTraceId_Iteration_Batc~",
                table: "ALLOCATION_TRACE_SCORE",
                columns: new[] { "AllocationLineTraceId", "Iteration", "BatchId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ALLOCATION_TRACE_CANDIDATE");

            migrationBuilder.DropTable(
                name: "ALLOCATION_TRACE_SCORE");

            migrationBuilder.DropTable(
                name: "ALLOCATION_LINE_TRACE");
        }
    }
}
