using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteRunPickingWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllocationTraceJson",
                table: "SALES_ORDER");

            migrationBuilder.AddColumn<Guid>(
                name: "CompletedByUserId",
                table: "RUN_PICKING_LIST",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExceptionNote",
                table: "RUN_PICKING_LIST",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExceptionType",
                table: "RUN_PICKING_LIST",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_LIST_CompletedByUserId",
                table: "RUN_PICKING_LIST",
                column: "CompletedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_RUN_PICKING_LIST_USER_ACCOUNT_CompletedByUserId",
                table: "RUN_PICKING_LIST",
                column: "CompletedByUserId",
                principalTable: "USER_ACCOUNT",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RUN_PICKING_LIST_USER_ACCOUNT_CompletedByUserId",
                table: "RUN_PICKING_LIST");

            migrationBuilder.DropIndex(
                name: "IX_RUN_PICKING_LIST_CompletedByUserId",
                table: "RUN_PICKING_LIST");

            migrationBuilder.DropColumn(
                name: "CompletedByUserId",
                table: "RUN_PICKING_LIST");

            migrationBuilder.DropColumn(
                name: "ExceptionNote",
                table: "RUN_PICKING_LIST");

            migrationBuilder.DropColumn(
                name: "ExceptionType",
                table: "RUN_PICKING_LIST");

            migrationBuilder.AddColumn<string>(
                name: "AllocationTraceJson",
                table: "SALES_ORDER",
                type: "jsonb",
                nullable: false,
                defaultValue: "");
        }
    }
}
