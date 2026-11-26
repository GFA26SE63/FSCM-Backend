using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    public partial class LinkWarehouseIssueToTransportDispatch : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceIssueDocumentId",
                table: "WAREHOUSE_TRANSFER",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_SourceIssueDocumentId",
                table: "WAREHOUSE_TRANSFER",
                column: "SourceIssueDocumentId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WAREHOUSE_TRANSFER_WAREHOUSE_DOCUMENT_SourceIssueDocumentId",
                table: "WAREHOUSE_TRANSFER",
                column: "SourceIssueDocumentId",
                principalTable: "WAREHOUSE_DOCUMENT",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WAREHOUSE_TRANSFER_WAREHOUSE_DOCUMENT_SourceIssueDocumentId",
                table: "WAREHOUSE_TRANSFER");

            migrationBuilder.DropIndex(
                name: "IX_WAREHOUSE_TRANSFER_SourceIssueDocumentId",
                table: "WAREHOUSE_TRANSFER");

            migrationBuilder.DropColumn(
                name: "SourceIssueDocumentId",
                table: "WAREHOUSE_TRANSFER");
        }
    }
}
