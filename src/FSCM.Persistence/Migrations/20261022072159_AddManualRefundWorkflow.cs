using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManualRefundWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdjustedAmount",
                table: "PAYMENT",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("UPDATE \"PAYMENT\" SET \"AdjustedAmount\" = \"Amount\";");

            migrationBuilder.CreateTable(
                name: "MANUAL_REFUND",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Recipient = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreateOperationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreateRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceOperationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    EvidenceRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TransferredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TransactionReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EvidenceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerificationOperationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    VerificationRequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    VerifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerificationNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MANUAL_REFUND", x => x.Id);
                    table.CheckConstraint("CK_MANUAL_REFUND_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_MANUAL_REFUND_PAYMENT_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "PAYMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MANUAL_REFUND_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MANUAL_REFUND_USER_ACCOUNT_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MANUAL_REFUND_USER_ACCOUNT_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MANUAL_REFUND_USER_ACCOUNT_VerifiedByUserId",
                        column: x => x.VerifiedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PAYMENT_AdjustedAmount",
                table: "PAYMENT",
                sql: "\"AdjustedAmount\" >= 0 AND \"AdjustedAmount\" <= \"Amount\"");

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_REFUND_CreatedByUserId",
                table: "MANUAL_REFUND",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_REFUND_CreateOperationKey",
                table: "MANUAL_REFUND",
                column: "CreateOperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_REFUND_EvidenceOperationKey",
                table: "MANUAL_REFUND",
                column: "EvidenceOperationKey",
                unique: true,
                filter: "\"EvidenceOperationKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_REFUND_PaymentId",
                table: "MANUAL_REFUND",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_REFUND_SalesOrderId_Status",
                table: "MANUAL_REFUND",
                columns: new[] { "SalesOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_REFUND_SubmittedByUserId",
                table: "MANUAL_REFUND",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_REFUND_VerificationOperationKey",
                table: "MANUAL_REFUND",
                column: "VerificationOperationKey",
                unique: true,
                filter: "\"VerificationOperationKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_REFUND_VerifiedByUserId",
                table: "MANUAL_REFUND",
                column: "VerifiedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MANUAL_REFUND");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PAYMENT_AdjustedAmount",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "AdjustedAmount",
                table: "PAYMENT");
        }
    }
}
