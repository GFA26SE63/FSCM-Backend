using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnCollectionRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RECEIPT_DISCREPANCY_ReportedByRetailerUserId",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.AddColumn<decimal>(
                name: "ApprovedQuantity",
                table: "RECEIPT_DISCREPANCY",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedSettlementMethod",
                table: "RECEIPT_DISCREPANCY",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DebtOffsetAmount",
                table: "RECEIPT_DISCREPANCY",
                type: "numeric(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "FinancialAdjustmentApplied",
                table: "RECEIPT_DISCREPANCY",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "HandledQuantity",
                table: "RECEIPT_DISCREPANCY",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PhysicalHandlingCompleted",
                table: "RECEIPT_DISCREPANCY",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundAmount",
                table: "RECEIPT_DISCREPANCY",
                type: "numeric(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "RefundId",
                table: "RECEIPT_DISCREPANCY",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "RECEIPT_DISCREPANCY",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "RECEIPT_DISCREPANCY",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestType",
                table: "RECEIPT_DISCREPANCY",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Complaint");

            migrationBuilder.AddColumn<decimal>(
                name: "RequestedQuantity",
                table: "RECEIPT_DISCREPANCY",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresCollection",
                table: "RECEIPT_DISCREPANCY",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SettlementMethod",
                table: "RECEIPT_DISCREPANCY",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "DebtOffset");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "RECEIPT_DISCREPANCY",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "RETURN_HANDLING_DOCUMENT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptDiscrepancyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ApprovedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ActualQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EvidenceReference = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RETURN_HANDLING_DOCUMENT", x => x.Id);
                    table.CheckConstraint("CK_RETURN_HANDLING_Kind", "\"Kind\" IN ('ReturnOrder','CollectionOrder')");
                    table.CheckConstraint("CK_RETURN_HANDLING_Quantity", "\"ApprovedQuantity\" > 0 AND (\"ActualQuantity\" IS NULL OR (\"ActualQuantity\" >= 0 AND \"ActualQuantity\" <= \"ApprovedQuantity\"))");
                    table.CheckConstraint("CK_RETURN_HANDLING_Status", "\"Status\" IN ('Pending','Completed','Waived')");
                    table.ForeignKey(
                        name: "FK_RETURN_HANDLING_DOCUMENT_RECEIPT_DISCREPANCY_ReceiptDiscrep~",
                        column: x => x.ReceiptDiscrepancyId,
                        principalTable: "RECEIPT_DISCREPANCY",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_DISCREPANCY_ReportedByRetailerUserId_RequestId",
                table: "RECEIPT_DISCREPANCY",
                columns: new[] { "ReportedByRetailerUserId", "RequestId" },
                unique: true,
                filter: "\"RequestId\" IS NOT NULL");

            // Preserve legacy quantities and distinguish a decision from an actually posted adjustment.
            migrationBuilder.Sql("""
                UPDATE "RECEIPT_DISCREPANCY" d SET "RequestedQuantity" = q.qty
                FROM (SELECT "ReceiptDiscrepancyId", SUM("ExpectedQuantity" - "ActualReceivedQuantity") qty
                      FROM "RECEIPT_DISCREPANCY_ITEM" GROUP BY "ReceiptDiscrepancyId") q
                WHERE d."Id" = q."ReceiptDiscrepancyId";
                UPDATE "RECEIPT_DISCREPANCY" d SET
                    "ApprovedQuantity" = CASE WHEN "RequestedQuantity" > 0 THEN "RequestedQuantity" ELSE NULL END,
                    "HandledQuantity" = CASE WHEN "RequestedQuantity" > 0 THEN "RequestedQuantity" ELSE NULL END,
                    "ApprovedSettlementMethod" = 'DebtOffset', "PhysicalHandlingCompleted" = TRUE,
                    "FinancialAdjustmentApplied" = EXISTS (
                        SELECT 1 FROM "PAYMENT" p WHERE p."SalesOrderId" = d."SalesOrderId" AND (
                            p."OperationKey" = 'complaint-adjust:' || replace(d."Id"::text, '-', '') OR EXISTS (
                                SELECT 1 FROM "AUDIT_LOG" a WHERE a."EntityId" = p."Id" AND a."Action" = 'ReceivableAdjusted'
                                AND a."Reason" LIKE '%KN-' || upper(left(replace(d."Id"::text, '-', ''),8)) || '%')))
                WHERE d."Status" IN (3,4) AND d."ResolutionType" = 1;
                UPDATE "RECEIPT_DISCREPANCY" SET "Status" = 3, "ResolvedAt" = NULL
                WHERE "Status" IN (3,4) AND "ResolutionType" = 1 AND NOT "FinancialAdjustmentApplied";
                -- Posted legacy adjustments stay closed. Refund obligations remain in the existing
                -- refund ledger; migration never issues money or invents an offset/refund split.
                UPDATE "RECEIPT_DISCREPANCY" SET "Status" = 4,
                    "ResolvedAt" = COALESCE("ResolvedAt", "ReviewedAt", "ReportedAt")
                WHERE "FinancialAdjustmentApplied";
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_REQUEST_Quantities",
                table: "RECEIPT_DISCREPANCY",
                sql: "\"RequestedQuantity\" >= 0 AND (\"ApprovedQuantity\" IS NULL OR (\"ApprovedQuantity\" > 0 AND \"ApprovedQuantity\" <= \"RequestedQuantity\")) AND (\"HandledQuantity\" IS NULL OR (\"HandledQuantity\" >= 0 AND \"HandledQuantity\" <= \"ApprovedQuantity\"))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_REQUEST_Settlement",
                table: "RECEIPT_DISCREPANCY",
                sql: "\"SettlementMethod\" IN ('DebtOffset','CashRefund','None') AND (\"ApprovedSettlementMethod\" IS NULL OR \"ApprovedSettlementMethod\" IN ('DebtOffset','CashRefund','None'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_REQUEST_Type",
                table: "RECEIPT_DISCREPANCY",
                sql: "\"RequestType\" IN ('Complaint','Return','DamagedCollection')");

            migrationBuilder.CreateIndex(
                name: "IX_RETURN_HANDLING_DOCUMENT_Code",
                table: "RETURN_HANDLING_DOCUMENT",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RETURN_HANDLING_DOCUMENT_ReceiptDiscrepancyId_Kind",
                table: "RETURN_HANDLING_DOCUMENT",
                columns: new[] { "ReceiptDiscrepancyId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RETURN_HANDLING_DOCUMENT");

            migrationBuilder.DropIndex(
                name: "IX_RECEIPT_DISCREPANCY_ReportedByRetailerUserId_RequestId",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropCheckConstraint(
                name: "CK_REQUEST_Quantities",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropCheckConstraint(
                name: "CK_REQUEST_Settlement",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropCheckConstraint(
                name: "CK_REQUEST_Type",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "ApprovedQuantity",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "ApprovedSettlementMethod",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "DebtOffsetAmount",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "FinancialAdjustmentApplied",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "HandledQuantity",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "PhysicalHandlingCompleted",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "RefundAmount",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "RefundId",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "RequestType",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "RequestedQuantity",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "RequiresCollection",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "SettlementMethod",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.CreateIndex(
                name: "IX_RECEIPT_DISCREPANCY_ReportedByRetailerUserId",
                table: "RECEIPT_DISCREPANCY",
                column: "ReportedByRetailerUserId");
        }
    }
}
