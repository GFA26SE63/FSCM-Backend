using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAllocationRunPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ALLOCATION_RUN_ALLOCATION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRunLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    EstimatedArrivalAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemainingShelfLifeHours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    WeightedScore = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ALLOCATION_RUN_ALLOCATION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_RUN_ALLOCATION_ALLOCATION_RUN_AllocationRunId",
                        column: x => x.AllocationRunId,
                        principalTable: "ALLOCATION_RUN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_RUN_ALLOCATION_ALLOCATION_RUN_LINE_AllocationRun~",
                        column: x => x.AllocationRunLineId,
                        principalTable: "ALLOCATION_RUN_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_RUN_ALLOCATION_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_ALLOCATION_AllocationRunId_BatchId",
                table: "ALLOCATION_RUN_ALLOCATION",
                columns: new[] { "AllocationRunId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_ALLOCATION_AllocationRunLineId_BatchId",
                table: "ALLOCATION_RUN_ALLOCATION",
                columns: new[] { "AllocationRunLineId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_ALLOCATION_BatchId",
                table: "ALLOCATION_RUN_ALLOCATION",
                column: "BatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ALLOCATION_RUN_ALLOCATION");
        }
    }
}
