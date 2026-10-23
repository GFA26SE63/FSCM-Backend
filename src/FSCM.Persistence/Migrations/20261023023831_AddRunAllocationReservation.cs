using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunAllocationReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RESERVATION_AllocationId",
                table: "RESERVATION");

            migrationBuilder.AlterColumn<Guid>(
                name: "AllocationId",
                table: "RESERVATION",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "AllocationRunAllocationId",
                table: "RESERVATION",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RESERVATION_AllocationId",
                table: "RESERVATION",
                column: "AllocationId",
                unique: true,
                filter: "\"AllocationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RESERVATION_AllocationRunAllocationId",
                table: "RESERVATION",
                column: "AllocationRunAllocationId",
                unique: true,
                filter: "\"AllocationRunAllocationId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_RESERVATION_ALLOCATION_RUN_ALLOCATION_AllocationRunAllocati~",
                table: "RESERVATION",
                column: "AllocationRunAllocationId",
                principalTable: "ALLOCATION_RUN_ALLOCATION",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RESERVATION_ALLOCATION_RUN_ALLOCATION_AllocationRunAllocati~",
                table: "RESERVATION");

            migrationBuilder.DropIndex(
                name: "IX_RESERVATION_AllocationId",
                table: "RESERVATION");

            migrationBuilder.DropIndex(
                name: "IX_RESERVATION_AllocationRunAllocationId",
                table: "RESERVATION");

            migrationBuilder.DropColumn(
                name: "AllocationRunAllocationId",
                table: "RESERVATION");

            migrationBuilder.AlterColumn<Guid>(
                name: "AllocationId",
                table: "RESERVATION",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RESERVATION_AllocationId",
                table: "RESERVATION",
                column: "AllocationId",
                unique: true);
        }
    }
}
