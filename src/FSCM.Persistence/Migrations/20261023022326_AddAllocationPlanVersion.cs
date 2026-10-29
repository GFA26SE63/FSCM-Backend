using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAllocationPlanVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ALLOCATION_RUN_ALLOCATION_AllocationRunLineId_BatchId",
                table: "ALLOCATION_RUN_ALLOCATION");

            migrationBuilder.AddColumn<Guid>(
                name: "AllocationPlanVersionId",
                table: "ALLOCATION_RUN_ALLOCATION",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "ALLOCATION_PLAN_VERSION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PricedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FulfilledOrderCount = table.Column<int>(type: "integer", nullable: false),
                    ShortOrderCount = table.Column<int>(type: "integer", nullable: false),
                    TotalFinalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecalculationRequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecalculationRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecalculationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ALLOCATION_PLAN_VERSION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_PLAN_VERSION_ALLOCATION_RUN_AllocationRunId",
                        column: x => x.AllocationRunId,
                        principalTable: "ALLOCATION_RUN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_PLAN_VERSION_USER_ACCOUNT_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ALLOCATION_PLAN_PRICE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationPlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRunLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    BaseNetUnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RemainingShelfLifePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ShelfLifeDiscountTierId = table.Column<Guid>(type: "uuid", nullable: true),
                    DiscountType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    DiscountValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ShelfLifeDiscountPerUnit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FinalNetUnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountTableVersion = table.Column<int>(type: "integer", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ALLOCATION_PLAN_PRICE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_PLAN_PRICE_ALLOCATION_PLAN_VERSION_AllocationPla~",
                        column: x => x.AllocationPlanVersionId,
                        principalTable: "ALLOCATION_PLAN_VERSION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ALLOCATION_PLAN_PRICE_ALLOCATION_RUN_LINE_AllocationRunLine~",
                        column: x => x.AllocationRunLineId,
                        principalTable: "ALLOCATION_RUN_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_ALLOCATION_AllocationPlanVersionId_Allocatio~",
                table: "ALLOCATION_RUN_ALLOCATION",
                columns: new[] { "AllocationPlanVersionId", "AllocationRunLineId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_ALLOCATION_AllocationRunLineId",
                table: "ALLOCATION_RUN_ALLOCATION",
                column: "AllocationRunLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_PLAN_PRICE_AllocationPlanVersionId_AllocationRun~",
                table: "ALLOCATION_PLAN_PRICE",
                columns: new[] { "AllocationPlanVersionId", "AllocationRunLineId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_PLAN_PRICE_AllocationRunLineId",
                table: "ALLOCATION_PLAN_PRICE",
                column: "AllocationRunLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_PLAN_VERSION_AllocationRunId",
                table: "ALLOCATION_PLAN_VERSION",
                column: "AllocationRunId",
                unique: true,
                filter: "\"Status\" = 'AwaitingApproval'");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_PLAN_VERSION_AllocationRunId_VersionNumber",
                table: "ALLOCATION_PLAN_VERSION",
                columns: new[] { "AllocationRunId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_PLAN_VERSION_ApprovedByUserId",
                table: "ALLOCATION_PLAN_VERSION",
                column: "ApprovedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ALLOCATION_RUN_ALLOCATION_ALLOCATION_PLAN_VERSION_Allocatio~",
                table: "ALLOCATION_RUN_ALLOCATION",
                column: "AllocationPlanVersionId",
                principalTable: "ALLOCATION_PLAN_VERSION",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ALLOCATION_RUN_ALLOCATION_ALLOCATION_PLAN_VERSION_Allocatio~",
                table: "ALLOCATION_RUN_ALLOCATION");

            migrationBuilder.DropTable(
                name: "ALLOCATION_PLAN_PRICE");

            migrationBuilder.DropTable(
                name: "ALLOCATION_PLAN_VERSION");

            migrationBuilder.DropIndex(
                name: "IX_ALLOCATION_RUN_ALLOCATION_AllocationPlanVersionId_Allocatio~",
                table: "ALLOCATION_RUN_ALLOCATION");

            migrationBuilder.DropIndex(
                name: "IX_ALLOCATION_RUN_ALLOCATION_AllocationRunLineId",
                table: "ALLOCATION_RUN_ALLOCATION");

            migrationBuilder.DropColumn(
                name: "AllocationPlanVersionId",
                table: "ALLOCATION_RUN_ALLOCATION");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_ALLOCATION_AllocationRunLineId_BatchId",
                table: "ALLOCATION_RUN_ALLOCATION",
                columns: new[] { "AllocationRunLineId", "BatchId" },
                unique: true);
        }
    }
}
