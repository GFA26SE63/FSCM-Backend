using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeReview2EnumsAndArchiveLegacyTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ALLOCATION_LINE_TRACE_ALLOCATION_PLAN_VERSION_AllocationPla~",
                table: "ALLOCATION_LINE_TRACE");

            migrationBuilder.DropForeignKey(
                name: "FK_ALLOCATION_PLAN_PRICE_ALLOCATION_PLAN_VERSION_AllocationPla~",
                table: "ALLOCATION_PLAN_PRICE");

            migrationBuilder.DropForeignKey(
                name: "FK_ALLOCATION_RUN_ALLOCATION_ALLOCATION_PLAN_VERSION_Allocatio~",
                table: "ALLOCATION_RUN_ALLOCATION");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_RUN_PICKING_LIST_RunPickingListId",
                table: "SHIPMENT");

            migrationBuilder.Sql("""
                INSERT INTO "RESERVATION" ("Id", "AllocationId", "AllocationRunAllocationId", "BatchId", "Quantity", "Status", "ReservedAt", "ReleasedAt", "ConsumedAt")
                SELECT "Id", "AllocationId", NULL, "BatchId", "Quantity",
                       CASE WHEN "Status" = 'Released' THEN 'Released' ELSE 'Active' END,
                       "HeldAt", "ReleasedAt", NULL
                FROM "STOCK_HOLD" h
                WHERE NOT EXISTS (SELECT 1 FROM "RESERVATION" r WHERE r."AllocationId" = h."AllocationId");

                INSERT INTO "PAYMENT_RECEIPT" ("Id", "ReceiptCode", "RetailerId", "ReceivedAmount", "UnappliedCreditAmount", "Status", "Reference", "Note", "ConfirmedByUserId", "ConfirmedAt", "ReversedByUserId", "ReversedAt", "ReversalReason", "OperationKey", "Version", "CreatedAt", "UpdatedAt")
                SELECT mp."Id", 'LEGACY-' || substr(replace(mp."Id"::text, '-', ''), 1, 12), so."RetailerId", mp."Amount", 0,
                       CASE WHEN mp."ConfirmedAt" IS NULL THEN 0 ELSE 1 END, mp."OperationKey", mp."Note",
                       mp."ConfirmedByUserId", mp."ConfirmedAt", NULL, NULL, NULL,
                       left('legacy-manual-' || mp."OperationKey", 80), mp."Version", mp."CreatedAt", mp."UpdatedAt"
                FROM "MANUAL_PAYMENT" mp
                JOIN "SALES_ORDER" so ON so."Id" = mp."SalesOrderId"
                WHERE NOT EXISTS (SELECT 1 FROM "PAYMENT_RECEIPT" pr WHERE pr."Id" = mp."Id");

                INSERT INTO "PAYMENT_RECEIPT_ALLOCATION" ("Id", "PaymentReceiptId", "PaymentId", "Amount", "CreatedAt")
                SELECT gen_random_uuid(), mp."Id", p."Id", LEAST(mp."Amount", p."AdjustedAmount"), mp."CreatedAt"
                FROM "MANUAL_PAYMENT" mp
                JOIN "PAYMENT" p ON p."SalesOrderId" = mp."SalesOrderId"
                WHERE mp."ConfirmedAt" IS NOT NULL AND mp."Amount" > 0
                  AND NOT EXISTS (SELECT 1 FROM "PAYMENT_RECEIPT_ALLOCATION" a WHERE a."PaymentReceiptId" = mp."Id");
                """);

            migrationBuilder.RenameTable(name: "BATCH_PROMOTION", newName: "HISTORY_BATCH_PROMOTION");
            migrationBuilder.RenameTable(name: "MANUAL_PAYMENT", newName: "HISTORY_MANUAL_PAYMENT");
            migrationBuilder.RenameTable(name: "RUN_PICKING_SPLIT", newName: "HISTORY_RUN_PICKING_SPLIT");
            migrationBuilder.RenameTable(name: "STOCK_HOLD", newName: "HISTORY_STOCK_HOLD");
            migrationBuilder.RenameTable(name: "WAREHOUSE_TRANSFER_EVENT", newName: "HISTORY_WAREHOUSE_TRANSFER_EVENT");
            migrationBuilder.RenameTable(name: "WAREHOUSE_TRANSFER_LABEL", newName: "HISTORY_WAREHOUSE_TRANSFER_LABEL");
            migrationBuilder.RenameTable(name: "RUN_PICKING_GATHER", newName: "HISTORY_RUN_PICKING_GATHER");
            migrationBuilder.RenameTable(name: "WAREHOUSE_TRANSFER_ITEM", newName: "HISTORY_WAREHOUSE_TRANSFER_ITEM");
            migrationBuilder.RenameTable(name: "RUN_PICKING_LIST", newName: "HISTORY_RUN_PICKING_LIST");
            migrationBuilder.RenameTable(name: "WAREHOUSE_TRANSFER", newName: "HISTORY_WAREHOUSE_TRANSFER");
            migrationBuilder.RenameTable(name: "ALLOCATION_PLAN_VERSION", newName: "HISTORY_ALLOCATION_PLAN_VERSION");

            migrationBuilder.Sql("""
                ALTER TABLE "HISTORY_ALLOCATION_PLAN_VERSION"
                    DROP CONSTRAINT "FK_ALLOCATION_PLAN_VERSION_ALLOCATION_RUN_AllocationRunId";
                CREATE TABLE "HISTORY_ALLOCATION_PLAN_PRICE" AS TABLE "ALLOCATION_PLAN_PRICE" WITH NO DATA;
                CREATE TABLE "HISTORY_ALLOCATION_LINE_TRACE" AS TABLE "ALLOCATION_LINE_TRACE" WITH NO DATA;
                CREATE TABLE "HISTORY_ALLOCATION_TRACE_CANDIDATE" AS TABLE "ALLOCATION_TRACE_CANDIDATE" WITH NO DATA;

                WITH ranked AS (
                    SELECT p."Id", row_number() OVER (
                        PARTITION BY v."AllocationRunId", p."AllocationRunLineId", p."BatchId"
                        ORDER BY v."VersionNumber" DESC, p."CreatedAt" DESC, p."Id") AS rn
                    FROM "ALLOCATION_PLAN_PRICE" p
                    JOIN "HISTORY_ALLOCATION_PLAN_VERSION" v ON v."Id" = p."AllocationPlanVersionId"
                )
                INSERT INTO "HISTORY_ALLOCATION_PLAN_PRICE"
                SELECT p.* FROM "ALLOCATION_PLAN_PRICE" p JOIN ranked r ON r."Id" = p."Id" WHERE r.rn > 1;
                WITH ranked AS (
                    SELECT p."Id", row_number() OVER (
                        PARTITION BY v."AllocationRunId", p."AllocationRunLineId", p."BatchId"
                        ORDER BY v."VersionNumber" DESC, p."CreatedAt" DESC, p."Id") AS rn
                    FROM "ALLOCATION_PLAN_PRICE" p
                    JOIN "HISTORY_ALLOCATION_PLAN_VERSION" v ON v."Id" = p."AllocationPlanVersionId"
                )
                DELETE FROM "ALLOCATION_PLAN_PRICE" p USING ranked r WHERE r."Id" = p."Id" AND r.rn > 1;

                WITH ranked AS (
                    SELECT t."Id", row_number() OVER (
                        PARTITION BY v."AllocationRunId", t."AllocationRunLineId"
                        ORDER BY v."VersionNumber" DESC, t."CreatedAt" DESC, t."Id") AS rn
                    FROM "ALLOCATION_LINE_TRACE" t
                    JOIN "HISTORY_ALLOCATION_PLAN_VERSION" v ON v."Id" = t."AllocationPlanVersionId"
                )
                INSERT INTO "HISTORY_ALLOCATION_LINE_TRACE"
                SELECT t.* FROM "ALLOCATION_LINE_TRACE" t JOIN ranked r ON r."Id" = t."Id" WHERE r.rn > 1;
                WITH ranked AS (
                    SELECT t."Id", row_number() OVER (
                        PARTITION BY v."AllocationRunId", t."AllocationRunLineId"
                        ORDER BY v."VersionNumber" DESC, t."CreatedAt" DESC, t."Id") AS rn
                    FROM "ALLOCATION_LINE_TRACE" t
                    JOIN "HISTORY_ALLOCATION_PLAN_VERSION" v ON v."Id" = t."AllocationPlanVersionId"
                )
                INSERT INTO "HISTORY_ALLOCATION_TRACE_CANDIDATE"
                SELECT c.* FROM "ALLOCATION_TRACE_CANDIDATE" c
                JOIN ranked r ON r."Id" = c."AllocationLineTraceId" WHERE r.rn > 1;
                WITH ranked AS (
                    SELECT t."Id", row_number() OVER (
                        PARTITION BY v."AllocationRunId", t."AllocationRunLineId"
                        ORDER BY v."VersionNumber" DESC, t."CreatedAt" DESC, t."Id") AS rn
                    FROM "ALLOCATION_LINE_TRACE" t
                    JOIN "HISTORY_ALLOCATION_PLAN_VERSION" v ON v."Id" = t."AllocationPlanVersionId"
                )
                DELETE FROM "ALLOCATION_TRACE_CANDIDATE" c USING ranked r
                WHERE r."Id" = c."AllocationLineTraceId" AND r.rn > 1;
                WITH ranked AS (
                    SELECT t."Id", row_number() OVER (
                        PARTITION BY v."AllocationRunId", t."AllocationRunLineId"
                        ORDER BY v."VersionNumber" DESC, t."CreatedAt" DESC, t."Id") AS rn
                    FROM "ALLOCATION_LINE_TRACE" t
                    JOIN "HISTORY_ALLOCATION_PLAN_VERSION" v ON v."Id" = t."AllocationPlanVersionId"
                )
                DELETE FROM "ALLOCATION_LINE_TRACE" t USING ranked r WHERE r."Id" = t."Id" AND r.rn > 1;

                UPDATE "ALLOCATION_PLAN_PRICE" p SET "AllocationPlanVersionId" = v."AllocationRunId"
                FROM "HISTORY_ALLOCATION_PLAN_VERSION" v WHERE p."AllocationPlanVersionId" = v."Id";
                UPDATE "ALLOCATION_LINE_TRACE" t SET "AllocationPlanVersionId" = v."AllocationRunId"
                FROM "HISTORY_ALLOCATION_PLAN_VERSION" v WHERE t."AllocationPlanVersionId" = v."Id";
                """);

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_RunPickingListId",
                table: "SHIPMENT");

            migrationBuilder.DropIndex(
                name: "IX_SALES_ORDER_QUOTE_AllocationPlanVersionId_Status",
                table: "SALES_ORDER_QUOTE");

            migrationBuilder.DropIndex(
                name: "IX_ALLOCATION_RUN_ALLOCATION_AllocationPlanVersionId_Allocatio~",
                table: "ALLOCATION_RUN_ALLOCATION");

            migrationBuilder.DropIndex(
                name: "IX_ALLOCATION_RUN_BusinessDate",
                table: "ALLOCATION_RUN");

            migrationBuilder.DropColumn(
                name: "RunPickingListId",
                table: "SHIPMENT");

            migrationBuilder.DropColumn(
                name: "AllocationPlanVersionId",
                table: "SALES_ORDER_QUOTE");

            migrationBuilder.DropColumn(
                name: "AllocationPlanVersionId",
                table: "ALLOCATION_RUN_ALLOCATION");

            migrationBuilder.DropColumn(
                name: "BusinessDate",
                table: "ALLOCATION_RUN");

            migrationBuilder.DropColumn(
                name: "CutoffAt",
                table: "ALLOCATION_RUN");

            migrationBuilder.DropColumn(
                name: "EtaScheduleVersion",
                table: "ALLOCATION_RUN");

            migrationBuilder.DropColumn(
                name: "OrderCount",
                table: "ALLOCATION_RUN");

            migrationBuilder.RenameColumn(
                name: "TransitTableVersion",
                table: "ALLOCATION_RUN",
                newName: "AttemptNumber");

            migrationBuilder.RenameColumn(
                name: "AllocationPlanVersionId",
                table: "ALLOCATION_PLAN_PRICE",
                newName: "AllocationRunId");

            migrationBuilder.RenameIndex(
                name: "IX_ALLOCATION_PLAN_PRICE_AllocationPlanVersionId_AllocationRun~",
                table: "ALLOCATION_PLAN_PRICE",
                newName: "IX_ALLOCATION_PLAN_PRICE_AllocationRunId_AllocationRunLineId_B~");

            migrationBuilder.RenameColumn(
                name: "AllocationPlanVersionId",
                table: "ALLOCATION_LINE_TRACE",
                newName: "AllocationRunId");

            migrationBuilder.RenameIndex(
                name: "IX_ALLOCATION_LINE_TRACE_AllocationPlanVersionId_AllocationRun~",
                table: "ALLOCATION_LINE_TRACE",
                newName: "IX_ALLOCATION_LINE_TRACE_AllocationRunId_AllocationRunLineId");

            migrationBuilder.AddColumn<Guid>(
                name: "PickingListId",
                table: "SHIPMENT",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE "SALES_ORDER_QUOTE" ALTER COLUMN "Status" TYPE smallint USING
                    CASE "Status" WHEN 'Accepted' THEN 1 WHEN 'Rejected' THEN 2 WHEN 'Expired' THEN 3 ELSE 0 END;
                ALTER TABLE "SALES_ORDER" ALTER COLUMN "Status" TYPE smallint USING
                    CASE "Status"
                        WHEN 'Quoted' THEN 1 WHEN 'Approved' THEN 1
                        WHEN 'AllocationFailed' THEN 2 WHEN 'OutOfStock' THEN 3 WHEN 'AllocationConflict' THEN 4
                        WHEN 'Withdrawn' THEN 5 WHEN 'Cancelled' THEN 5
                        WHEN 'QuoteRejected' THEN 6 WHEN 'Rejected' THEN 6 WHEN 'QuoteExpired' THEN 7
                        WHEN 'PaymentPending' THEN 8
                        WHEN 'ConvertedToPo' THEN 9 WHEN 'Picking' THEN 9 WHEN 'Issued' THEN 9
                        WHEN 'Delivering' THEN 9 WHEN 'Delivered' THEN 9 WHEN 'Completed' THEN 9
                        ELSE 0 END;
                ALTER TABLE "SALES_ORDER" ALTER COLUMN "PaymentMethod" TYPE smallint USING
                    CASE WHEN "PaymentMethod" = 'Prepaid' THEN 0 ELSE 1 END;
                ALTER TABLE "RESERVATION" ALTER COLUMN "Status" TYPE smallint USING
                    CASE "Status" WHEN 'Released' THEN 1 WHEN 'Consumed' THEN 2 WHEN 'Expired' THEN 3 ELSE 0 END;
                ALTER TABLE "RECEIPT_DISCREPANCY" ALTER COLUMN "Status" TYPE smallint USING
                    CASE "Status" WHEN 'UnderReview' THEN 1 WHEN 'Rejected' THEN 2 WHEN 'Approved' THEN 3 WHEN 'Resolved' THEN 4 ELSE 0 END;
                ALTER TABLE "PICKING_LIST" ALTER COLUMN "Status" TYPE smallint USING
                    CASE "Status" WHEN 'InProgress' THEN 1 WHEN 'Completed' THEN 2 WHEN 'Exception' THEN 3 WHEN 'Cancelled' THEN 3 ELSE 0 END;
                ALTER TABLE "PAYMENT" ALTER COLUMN "Status" TYPE smallint USING
                    CASE "Status" WHEN 'PartiallyPaid' THEN 1 WHEN 'Paid' THEN 2 WHEN 'Overdue' THEN 3 ELSE 0 END;
                ALTER TABLE "PAYMENT" ALTER COLUMN "Method" TYPE smallint USING
                    CASE WHEN "Method" = 'Prepaid' THEN 0 ELSE 1 END;
                ALTER TABLE "INVENTORY_TRANSACTION" ALTER COLUMN "TransactionType" TYPE smallint USING
                    CASE "TransactionType"
                        WHEN 'GoodsReceipt' THEN 0 WHEN 'PurchaseReceipt' THEN 0 WHEN 'CustomerReturnReceipt' THEN 1
                        WHEN 'AdjustmentIncrease' THEN 2 WHEN 'TransferReceipt' THEN 2
                        WHEN 'OrderIssue' THEN 3 WHEN 'SalesIssue' THEN 3 WHEN 'ReplacementIssue' THEN 4
                        WHEN 'WriteOff' THEN 5 WHEN 'AdjustmentDecrease' THEN 6 WHEN 'TransferDispatch' THEN 6
                        WHEN 'OrderReserve' THEN 7 WHEN 'OrderRelease' THEN 8 ELSE 6 END;
                ALTER TABLE "ALLOCATION_RUN" ALTER COLUMN "Status" TYPE smallint USING
                    CASE "Status" WHEN 'OutOfStock' THEN 2 WHEN 'Conflict' THEN 3
                        WHEN 'Failed' THEN 4 WHEN 'TechnicalFailed' THEN 4
                        WHEN 'Approved' THEN 1 WHEN 'Closed' THEN 1 WHEN 'AwaitingApproval' THEN 1 ELSE 0 END;
                """);

            migrationBuilder.AlterColumn<short>(
                name: "Status",
                table: "SALES_ORDER_QUOTE",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<short>(
                name: "Status",
                table: "SALES_ORDER",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<short>(
                name: "PaymentMethod",
                table: "SALES_ORDER",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AcceptedAt",
                table: "SALES_ORDER",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AlterColumn<short>(
                name: "Status",
                table: "RESERVATION",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<short>(
                name: "Status",
                table: "RECEIPT_DISCREPANCY",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddColumn<short>(
                name: "ResolutionType",
                table: "RECEIPT_DISCREPANCY",
                type: "smallint",
                nullable: true);

            migrationBuilder.AlterColumn<short>(
                name: "Status",
                table: "PICKING_LIST",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<short>(
                name: "Status",
                table: "PAYMENT",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<short>(
                name: "Method",
                table: "PAYMENT",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<short>(
                name: "TransactionType",
                table: "INVENTORY_TRANSACTION",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<short>(
                name: "Status",
                table: "ALLOCATION_RUN",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddColumn<Guid>(
                name: "RetryOfAllocationRunId",
                table: "ALLOCATION_RUN",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesOrderId",
                table: "ALLOCATION_RUN",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "TechnicalErrorCode",
                table: "ALLOCATION_RUN",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "SALES_ORDER" SET "AcceptedAt" = "ServerReceivedAt";

                CREATE TABLE IF NOT EXISTS "HISTORY_ORPHAN_ALLOCATION_RUN" AS
                    SELECT r.* FROM "ALLOCATION_RUN" r
                    WHERE NOT EXISTS (SELECT 1 FROM "ALLOCATION_RUN_ORDER" o WHERE o."AllocationRunId" = r."Id");
                DELETE FROM "ALLOCATION_RUN" r
                    WHERE NOT EXISTS (SELECT 1 FROM "ALLOCATION_RUN_ORDER" o WHERE o."AllocationRunId" = r."Id");

                UPDATE "ALLOCATION_RUN" r SET "SalesOrderId" = source."SalesOrderId"
                FROM (
                    SELECT DISTINCT ON ("AllocationRunId") "AllocationRunId", "SalesOrderId"
                    FROM "ALLOCATION_RUN_ORDER" ORDER BY "AllocationRunId", "CreatedAt", "Id"
                ) source WHERE source."AllocationRunId" = r."Id";

                WITH numbered AS (
                    SELECT "Id", row_number() OVER (PARTITION BY "SalesOrderId" ORDER BY "StartedAt", "Id") AS number
                    FROM "ALLOCATION_RUN"
                )
                UPDATE "ALLOCATION_RUN" r SET "AttemptNumber" = numbered.number
                FROM numbered WHERE numbered."Id" = r."Id";

                UPDATE "SHIPMENT" s SET "PickingListId" = p."Id"
                FROM "PICKING_LIST" p
                WHERE p."SalesOrderId" = s."SalesOrderId" AND p."WarehouseId" = s."WarehouseId";

                CREATE TEMP TABLE allocation_run_split ON COMMIT DROP AS
                SELECT o."AllocationRunId" AS old_run_id, o."Id" AS run_order_id, o."SalesOrderId" AS sales_order_id,
                       row_number() OVER (PARTITION BY o."AllocationRunId" ORDER BY o."CreatedAt", o."Id") AS ordinal,
                       CASE WHEN row_number() OVER (PARTITION BY o."AllocationRunId" ORDER BY o."CreatedAt", o."Id") = 1
                            THEN o."AllocationRunId" ELSE gen_random_uuid() END AS new_run_id
                FROM "ALLOCATION_RUN_ORDER" o;

                INSERT INTO "ALLOCATION_RUN" ("Id", "RunCode", "SalesOrderId", "AttemptNumber", "RetryOfAllocationRunId", "Status", "AlgorithmSettings", "FailureReason", "TechnicalErrorCode", "StartedAt", "SnapshotCompletedAt", "CompletedAt", "Version", "CreatedAt", "UpdatedAt")
                SELECT m.new_run_id, left(r."RunCode", 30) || '-' || substr(replace(m.sales_order_id::text, '-', ''), 1, 8),
                       m.sales_order_id, 1, NULL, r."Status", r."AlgorithmSettings", r."FailureReason", r."TechnicalErrorCode",
                       r."StartedAt", r."SnapshotCompletedAt", r."CompletedAt", r."Version", r."CreatedAt", r."UpdatedAt"
                FROM allocation_run_split m JOIN "ALLOCATION_RUN" r ON r."Id" = m.old_run_id
                WHERE m.ordinal > 1;

                UPDATE "ALLOCATION_RUN_ORDER" o SET "AllocationRunId" = m.new_run_id
                FROM allocation_run_split m WHERE m.run_order_id = o."Id";
                UPDATE "ALLOCATION_RUN_ALLOCATION" a SET "AllocationRunId" = o."AllocationRunId"
                FROM "ALLOCATION_RUN_LINE" l JOIN "ALLOCATION_RUN_ORDER" o ON o."Id" = l."AllocationRunOrderId"
                WHERE l."Id" = a."AllocationRunLineId";
                UPDATE "ALLOCATION_PLAN_PRICE" p SET "AllocationRunId" = o."AllocationRunId"
                FROM "ALLOCATION_RUN_LINE" l JOIN "ALLOCATION_RUN_ORDER" o ON o."Id" = l."AllocationRunOrderId"
                WHERE l."Id" = p."AllocationRunLineId";
                UPDATE "ALLOCATION_LINE_TRACE" t SET "AllocationRunId" = o."AllocationRunId"
                FROM "ALLOCATION_RUN_LINE" l JOIN "ALLOCATION_RUN_ORDER" o ON o."Id" = l."AllocationRunOrderId"
                WHERE l."Id" = t."AllocationRunLineId";
                UPDATE "SALES_ORDER_QUOTE" q SET "AllocationRunId" = o."AllocationRunId"
                FROM "ALLOCATION_RUN_ORDER" o WHERE o."SalesOrderId" = q."SalesOrderId" AND o."AllocationRunId" IN
                    (SELECT new_run_id FROM allocation_run_split WHERE old_run_id = q."AllocationRunId");

                WITH numbered AS (
                    SELECT "Id", row_number() OVER (PARTITION BY "SalesOrderId" ORDER BY "StartedAt", "Id") AS number
                    FROM "ALLOCATION_RUN"
                ) UPDATE "ALLOCATION_RUN" r SET "AttemptNumber" = numbered.number
                  FROM numbered WHERE numbered."Id" = r."Id";
                """);

            migrationBuilder.CreateTable(
                name: "REPLACEMENT_CASE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptDiscrepancyId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ShippedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REPLACEMENT_CASE", x => x.Id);
                    table.CheckConstraint("CK_REPLACEMENT_CASE_Status", "\"Status\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_REPLACEMENT_CASE_RECEIPT_DISCREPANCY_ReceiptDiscrepancyId",
                        column: x => x.ReceiptDiscrepancyId,
                        principalTable: "RECEIPT_DISCREPANCY",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_REPLACEMENT_CASE_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_PickingListId",
                table: "SHIPMENT",
                column: "PickingListId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_QUOTE_AllocationRunId_Status",
                table: "SALES_ORDER_QUOTE",
                columns: new[] { "AllocationRunId", "Status" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SALES_ORDER_QUOTE_Status",
                table: "SALES_ORDER_QUOTE",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SALES_ORDER_PaymentMethod",
                table: "SALES_ORDER",
                sql: "\"PaymentMethod\" BETWEEN 0 AND 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SALES_ORDER_Status",
                table: "SALES_ORDER",
                sql: "\"Status\" BETWEEN 0 AND 9");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RESERVATION_Status",
                table: "RESERVATION",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RECEIPT_DISCREPANCY_ResolutionType",
                table: "RECEIPT_DISCREPANCY",
                sql: "\"ResolutionType\" IS NULL OR \"ResolutionType\" BETWEEN 0 AND 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RECEIPT_DISCREPANCY_Status",
                table: "RECEIPT_DISCREPANCY",
                sql: "\"Status\" BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PICKING_LIST_Status",
                table: "PICKING_LIST",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PAYMENT_Method",
                table: "PAYMENT",
                sql: "\"Method\" BETWEEN 0 AND 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PAYMENT_Status",
                table: "PAYMENT",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_INVENTORY_TRANSACTION_TransactionType",
                table: "INVENTORY_TRANSACTION",
                sql: "\"TransactionType\" BETWEEN 0 AND 8");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_ALLOCATION_AllocationRunId_AllocationRunLine~",
                table: "ALLOCATION_RUN_ALLOCATION",
                columns: new[] { "AllocationRunId", "AllocationRunLineId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_RetryOfAllocationRunId",
                table: "ALLOCATION_RUN",
                column: "RetryOfAllocationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_SalesOrderId_AttemptNumber",
                table: "ALLOCATION_RUN",
                columns: new[] { "SalesOrderId", "AttemptNumber" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ALLOCATION_RUN_Status",
                table: "ALLOCATION_RUN",
                sql: "\"Status\" BETWEEN 0 AND 4");

            migrationBuilder.CreateIndex(
                name: "IX_REPLACEMENT_CASE_ReceiptDiscrepancyId",
                table: "REPLACEMENT_CASE",
                column: "ReceiptDiscrepancyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_REPLACEMENT_CASE_WarehouseId",
                table: "REPLACEMENT_CASE",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_ALLOCATION_LINE_TRACE_ALLOCATION_RUN_AllocationRunId",
                table: "ALLOCATION_LINE_TRACE",
                column: "AllocationRunId",
                principalTable: "ALLOCATION_RUN",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ALLOCATION_PLAN_PRICE_ALLOCATION_RUN_AllocationRunId",
                table: "ALLOCATION_PLAN_PRICE",
                column: "AllocationRunId",
                principalTable: "ALLOCATION_RUN",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ALLOCATION_RUN_ALLOCATION_RUN_RetryOfAllocationRunId",
                table: "ALLOCATION_RUN",
                column: "RetryOfAllocationRunId",
                principalTable: "ALLOCATION_RUN",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ALLOCATION_RUN_SALES_ORDER_SalesOrderId",
                table: "ALLOCATION_RUN",
                column: "SalesOrderId",
                principalTable: "SALES_ORDER",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_PICKING_LIST_PickingListId",
                table: "SHIPMENT",
                column: "PickingListId",
                principalTable: "PICKING_LIST",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider is not null)
                throw new NotSupportedException("Review 2 schema normalization is forward-only because legacy rows are archived for audit.");
            migrationBuilder.DropForeignKey(
                name: "FK_ALLOCATION_LINE_TRACE_ALLOCATION_RUN_AllocationRunId",
                table: "ALLOCATION_LINE_TRACE");

            migrationBuilder.DropForeignKey(
                name: "FK_ALLOCATION_PLAN_PRICE_ALLOCATION_RUN_AllocationRunId",
                table: "ALLOCATION_PLAN_PRICE");

            migrationBuilder.DropForeignKey(
                name: "FK_ALLOCATION_RUN_ALLOCATION_RUN_RetryOfAllocationRunId",
                table: "ALLOCATION_RUN");

            migrationBuilder.DropForeignKey(
                name: "FK_ALLOCATION_RUN_SALES_ORDER_SalesOrderId",
                table: "ALLOCATION_RUN");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_PICKING_LIST_PickingListId",
                table: "SHIPMENT");

            migrationBuilder.DropTable(
                name: "REPLACEMENT_CASE");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_PickingListId",
                table: "SHIPMENT");

            migrationBuilder.DropIndex(
                name: "IX_SALES_ORDER_QUOTE_AllocationRunId_Status",
                table: "SALES_ORDER_QUOTE");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SALES_ORDER_QUOTE_Status",
                table: "SALES_ORDER_QUOTE");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SALES_ORDER_PaymentMethod",
                table: "SALES_ORDER");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SALES_ORDER_Status",
                table: "SALES_ORDER");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RESERVATION_Status",
                table: "RESERVATION");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RECEIPT_DISCREPANCY_ResolutionType",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RECEIPT_DISCREPANCY_Status",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PICKING_LIST_Status",
                table: "PICKING_LIST");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PAYMENT_Method",
                table: "PAYMENT");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PAYMENT_Status",
                table: "PAYMENT");

            migrationBuilder.DropCheckConstraint(
                name: "CK_INVENTORY_TRANSACTION_TransactionType",
                table: "INVENTORY_TRANSACTION");

            migrationBuilder.DropIndex(
                name: "IX_ALLOCATION_RUN_ALLOCATION_AllocationRunId_AllocationRunLine~",
                table: "ALLOCATION_RUN_ALLOCATION");

            migrationBuilder.DropIndex(
                name: "IX_ALLOCATION_RUN_RetryOfAllocationRunId",
                table: "ALLOCATION_RUN");

            migrationBuilder.DropIndex(
                name: "IX_ALLOCATION_RUN_SalesOrderId_AttemptNumber",
                table: "ALLOCATION_RUN");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ALLOCATION_RUN_Status",
                table: "ALLOCATION_RUN");

            migrationBuilder.DropColumn(
                name: "PickingListId",
                table: "SHIPMENT");

            migrationBuilder.DropColumn(
                name: "AcceptedAt",
                table: "SALES_ORDER");

            migrationBuilder.DropColumn(
                name: "ResolutionType",
                table: "RECEIPT_DISCREPANCY");

            migrationBuilder.DropColumn(
                name: "RetryOfAllocationRunId",
                table: "ALLOCATION_RUN");

            migrationBuilder.DropColumn(
                name: "SalesOrderId",
                table: "ALLOCATION_RUN");

            migrationBuilder.DropColumn(
                name: "TechnicalErrorCode",
                table: "ALLOCATION_RUN");

            migrationBuilder.RenameColumn(
                name: "AttemptNumber",
                table: "ALLOCATION_RUN",
                newName: "TransitTableVersion");

            migrationBuilder.RenameColumn(
                name: "AllocationRunId",
                table: "ALLOCATION_PLAN_PRICE",
                newName: "AllocationPlanVersionId");

            migrationBuilder.RenameIndex(
                name: "IX_ALLOCATION_PLAN_PRICE_AllocationRunId_AllocationRunLineId_B~",
                table: "ALLOCATION_PLAN_PRICE",
                newName: "IX_ALLOCATION_PLAN_PRICE_AllocationPlanVersionId_AllocationRun~");

            migrationBuilder.RenameColumn(
                name: "AllocationRunId",
                table: "ALLOCATION_LINE_TRACE",
                newName: "AllocationPlanVersionId");

            migrationBuilder.RenameIndex(
                name: "IX_ALLOCATION_LINE_TRACE_AllocationRunId_AllocationRunLineId",
                table: "ALLOCATION_LINE_TRACE",
                newName: "IX_ALLOCATION_LINE_TRACE_AllocationPlanVersionId_AllocationRun~");

            migrationBuilder.AddColumn<Guid>(
                name: "RunPickingListId",
                table: "SHIPMENT",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "SALES_ORDER_QUOTE",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AddColumn<Guid>(
                name: "AllocationPlanVersionId",
                table: "SALES_ORDER_QUOTE",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "SALES_ORDER",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AlterColumn<string>(
                name: "PaymentMethod",
                table: "SALES_ORDER",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "RESERVATION",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "RECEIPT_DISCREPANCY",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "PICKING_LIST",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "PAYMENT",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AlterColumn<string>(
                name: "Method",
                table: "PAYMENT",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AlterColumn<string>(
                name: "TransactionType",
                table: "INVENTORY_TRANSACTION",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AddColumn<Guid>(
                name: "AllocationPlanVersionId",
                table: "ALLOCATION_RUN_ALLOCATION",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ALLOCATION_RUN",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AddColumn<DateOnly>(
                name: "BusinessDate",
                table: "ALLOCATION_RUN",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CutoffAt",
                table: "ALLOCATION_RUN",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "EtaScheduleVersion",
                table: "ALLOCATION_RUN",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrderCount",
                table: "ALLOCATION_RUN",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ALLOCATION_PLAN_VERSION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FulfilledOrderCount = table.Column<int>(type: "integer", nullable: false),
                    PricedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecalculationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RecalculationRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecalculationRequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ShortOrderCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TotalFinalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false)
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
                name: "BATCH_PROMOTION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BATCH_PROMOTION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BATCH_PROMOTION_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BATCH_PROMOTION_PROMOTION_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "PROMOTION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BATCH_PROMOTION_USER_ACCOUNT_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MANUAL_PAYMENT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    OperationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MANUAL_PAYMENT", x => x.Id);
                    table.CheckConstraint("CK_MANUAL_PAYMENT_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_MANUAL_PAYMENT_SALES_ORDER_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SALES_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MANUAL_PAYMENT_USER_ACCOUNT_ConfirmedByUserId",
                        column: x => x.ConfirmedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "STOCK_HOLD",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    HeldAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STOCK_HOLD", x => x.Id);
                    table.ForeignKey(
                        name: "FK_STOCK_HOLD_ALLOCATION_AllocationId",
                        column: x => x.AllocationId,
                        principalTable: "ALLOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_STOCK_HOLD_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_TRANSFER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TransferCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_TRANSFER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_USER_ACCOUNT_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_USER_ACCOUNT_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_WAREHOUSE_DestinationWarehouseId",
                        column: x => x.DestinationWarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_WAREHOUSE_SourceWarehouseId",
                        column: x => x.SourceWarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RUN_PICKING_LIST",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationPlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExceptionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExceptionType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PickingCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RUN_PICKING_LIST", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_LIST_ALLOCATION_PLAN_VERSION_AllocationPlanVers~",
                        column: x => x.AllocationPlanVersionId,
                        principalTable: "ALLOCATION_PLAN_VERSION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_LIST_ALLOCATION_RUN_AllocationRunId",
                        column: x => x.AllocationRunId,
                        principalTable: "ALLOCATION_RUN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_LIST_USER_ACCOUNT_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_LIST_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_TRANSFER_EVENT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OperationKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    WarehouseTransferId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_TRANSFER_EVENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_EVENT_USER_ACCOUNT_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_EVENT_WAREHOUSE_TRANSFER_WarehouseTransf~",
                        column: x => x.WarehouseTransferId,
                        principalTable: "WAREHOUSE_TRANSFER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_TRANSFER_ITEM",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DestinationBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    SourceBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WarehouseTransferId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_TRANSFER_ITEM", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_ITEM_BATCH_DestinationBatchId",
                        column: x => x.DestinationBatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_ITEM_BATCH_SourceBatchId",
                        column: x => x.SourceBatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_ITEM_WAREHOUSE_TRANSFER_WarehouseTransfe~",
                        column: x => x.WarehouseTransferId,
                        principalTable: "WAREHOUSE_TRANSFER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RUN_PICKING_GATHER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchCodeSnapshot = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequiredQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    RunPickingListId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkuSnapshot = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RUN_PICKING_GATHER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_GATHER_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_GATHER_RUN_PICKING_LIST_RunPickingListId",
                        column: x => x.RunPickingListId,
                        principalTable: "RUN_PICKING_LIST",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_TRANSFER_LABEL",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchQrLabelId = table.Column<Guid>(type: "uuid", nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WarehouseTransferItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_TRANSFER_LABEL", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_LABEL_BATCH_QR_LABEL_BatchQrLabelId",
                        column: x => x.BatchQrLabelId,
                        principalTable: "BATCH_QR_LABEL",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_LABEL_WAREHOUSE_TRANSFER_ITEM_WarehouseT~",
                        column: x => x.WarehouseTransferItemId,
                        principalTable: "WAREHOUSE_TRANSFER_ITEM",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RUN_PICKING_SPLIT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationPlanPriceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRunAllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRunLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OrderCodeSnapshot = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    RunPickingGatherId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderLineId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RUN_PICKING_SPLIT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_SPLIT_ALLOCATION_PLAN_PRICE_AllocationPlanPrice~",
                        column: x => x.AllocationPlanPriceId,
                        principalTable: "ALLOCATION_PLAN_PRICE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_SPLIT_ALLOCATION_RUN_ALLOCATION_AllocationRunAl~",
                        column: x => x.AllocationRunAllocationId,
                        principalTable: "ALLOCATION_RUN_ALLOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RUN_PICKING_SPLIT_RUN_PICKING_GATHER_RunPickingGatherId",
                        column: x => x.RunPickingGatherId,
                        principalTable: "RUN_PICKING_GATHER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_RunPickingListId",
                table: "SHIPMENT",
                column: "RunPickingListId");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_QUOTE_AllocationPlanVersionId_Status",
                table: "SALES_ORDER_QUOTE",
                columns: new[] { "AllocationPlanVersionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_ALLOCATION_AllocationPlanVersionId_Allocatio~",
                table: "ALLOCATION_RUN_ALLOCATION",
                columns: new[] { "AllocationPlanVersionId", "AllocationRunLineId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ALLOCATION_RUN_BusinessDate",
                table: "ALLOCATION_RUN",
                column: "BusinessDate",
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_PROMOTION_AssignedByUserId",
                table: "BATCH_PROMOTION",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_PROMOTION_BatchId_PromotionId",
                table: "BATCH_PROMOTION",
                columns: new[] { "BatchId", "PromotionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_PROMOTION_PromotionId",
                table: "BATCH_PROMOTION",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_PAYMENT_ConfirmedByUserId",
                table: "MANUAL_PAYMENT",
                column: "ConfirmedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_PAYMENT_OperationKey",
                table: "MANUAL_PAYMENT",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_PAYMENT_SalesOrderId",
                table: "MANUAL_PAYMENT",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_PAYMENT_SalesOrderId_Status",
                table: "MANUAL_PAYMENT",
                columns: new[] { "SalesOrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_GATHER_BatchId",
                table: "RUN_PICKING_GATHER",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_GATHER_RunPickingListId_BatchId",
                table: "RUN_PICKING_GATHER",
                columns: new[] { "RunPickingListId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_LIST_AllocationPlanVersionId",
                table: "RUN_PICKING_LIST",
                column: "AllocationPlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_LIST_AllocationRunId_WarehouseId",
                table: "RUN_PICKING_LIST",
                columns: new[] { "AllocationRunId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_LIST_CompletedByUserId",
                table: "RUN_PICKING_LIST",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_LIST_PickingCode",
                table: "RUN_PICKING_LIST",
                column: "PickingCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_LIST_WarehouseId",
                table: "RUN_PICKING_LIST",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_SPLIT_AllocationPlanPriceId",
                table: "RUN_PICKING_SPLIT",
                column: "AllocationPlanPriceId");

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_SPLIT_AllocationRunAllocationId",
                table: "RUN_PICKING_SPLIT",
                column: "AllocationRunAllocationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RUN_PICKING_SPLIT_RunPickingGatherId",
                table: "RUN_PICKING_SPLIT",
                column: "RunPickingGatherId");

            migrationBuilder.CreateIndex(
                name: "IX_STOCK_HOLD_AllocationId",
                table: "STOCK_HOLD",
                column: "AllocationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_STOCK_HOLD_BatchId",
                table: "STOCK_HOLD",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_ApprovedByUserId",
                table: "WAREHOUSE_TRANSFER",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_CreatedByUserId",
                table: "WAREHOUSE_TRANSFER",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_DestinationWarehouseId",
                table: "WAREHOUSE_TRANSFER",
                column: "DestinationWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_SourceWarehouseId",
                table: "WAREHOUSE_TRANSFER",
                column: "SourceWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_TransferCode",
                table: "WAREHOUSE_TRANSFER",
                column: "TransferCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_EVENT_OperationKey",
                table: "WAREHOUSE_TRANSFER_EVENT",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_EVENT_PerformedByUserId",
                table: "WAREHOUSE_TRANSFER_EVENT",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_EVENT_WarehouseTransferId_OccurredAt",
                table: "WAREHOUSE_TRANSFER_EVENT",
                columns: new[] { "WarehouseTransferId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_ITEM_DestinationBatchId",
                table: "WAREHOUSE_TRANSFER_ITEM",
                column: "DestinationBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_ITEM_SourceBatchId",
                table: "WAREHOUSE_TRANSFER_ITEM",
                column: "SourceBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_ITEM_WarehouseTransferId",
                table: "WAREHOUSE_TRANSFER_ITEM",
                column: "WarehouseTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_LABEL_BatchQrLabelId",
                table: "WAREHOUSE_TRANSFER_LABEL",
                column: "BatchQrLabelId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_LABEL_WarehouseTransferItemId_BatchQrLab~",
                table: "WAREHOUSE_TRANSFER_LABEL",
                columns: new[] { "WarehouseTransferItemId", "BatchQrLabelId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ALLOCATION_LINE_TRACE_ALLOCATION_PLAN_VERSION_AllocationPla~",
                table: "ALLOCATION_LINE_TRACE",
                column: "AllocationPlanVersionId",
                principalTable: "ALLOCATION_PLAN_VERSION",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ALLOCATION_PLAN_PRICE_ALLOCATION_PLAN_VERSION_AllocationPla~",
                table: "ALLOCATION_PLAN_PRICE",
                column: "AllocationPlanVersionId",
                principalTable: "ALLOCATION_PLAN_VERSION",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ALLOCATION_RUN_ALLOCATION_ALLOCATION_PLAN_VERSION_Allocatio~",
                table: "ALLOCATION_RUN_ALLOCATION",
                column: "AllocationPlanVersionId",
                principalTable: "ALLOCATION_PLAN_VERSION",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_RUN_PICKING_LIST_RunPickingListId",
                table: "SHIPMENT",
                column: "RunPickingListId",
                principalTable: "RUN_PICKING_LIST",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
