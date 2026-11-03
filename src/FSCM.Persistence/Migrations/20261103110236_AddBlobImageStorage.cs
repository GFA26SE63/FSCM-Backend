using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBlobImageStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StorefrontImageBlobKey",
                table: "RETAILER",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageBlobKey",
                table: "PRODUCT",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "COMPLAINT_EVIDENCE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptDiscrepancyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BlobKey = table.Column<string>(type: "text", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COMPLAINT_EVIDENCE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_COMPLAINT_EVIDENCE_RECEIPT_DISCREPANCY_ReceiptDiscrepancyId",
                        column: x => x.ReceiptDiscrepancyId,
                        principalTable: "RECEIPT_DISCREPANCY",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_COMPLAINT_EVIDENCE_USER_ACCOUNT_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_COMPLAINT_EVIDENCE_ReceiptDiscrepancyId",
                table: "COMPLAINT_EVIDENCE",
                column: "ReceiptDiscrepancyId");

            migrationBuilder.CreateIndex(
                name: "IX_COMPLAINT_EVIDENCE_UploadedByUserId",
                table: "COMPLAINT_EVIDENCE",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "COMPLAINT_EVIDENCE");

            migrationBuilder.DropColumn(
                name: "StorefrontImageBlobKey",
                table: "RETAILER");

            migrationBuilder.DropColumn(
                name: "ImageBlobKey",
                table: "PRODUCT");
        }
    }
}
