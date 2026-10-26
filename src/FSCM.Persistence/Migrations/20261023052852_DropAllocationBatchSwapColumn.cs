using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropAllocationBatchSwapColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SwappedFromBatchId",
                table: "ALLOCATION_RUN_ALLOCATION");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SwappedFromBatchId",
                table: "ALLOCATION_RUN_ALLOCATION",
                type: "uuid",
                nullable: true);
        }
    }
}
