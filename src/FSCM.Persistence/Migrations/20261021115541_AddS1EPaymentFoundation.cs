using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddS1EPaymentFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateOnly>(
                name: "DeliveredDate",
                table: "PAYMENT",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<decimal>(
                name: "ConfirmedAmount",
                table: "PAYMENT",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "FinalApprovalReference",
                table: "PAYMENT",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FinalApprovedAt",
                table: "PAYMENT",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "PayOsWebhookHash",
                table: "PAYMENT",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationKey",
                table: "MANUAL_PAYMENT",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "MANUAL_PAYMENT",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "MANUAL_PAYMENT",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE "MANUAL_PAYMENT"
                SET "OperationKey" = 'legacy-manual:' || "Id"::text,
                    "RequestHash" = replace("Id"::text, '-', '')
                WHERE "OperationKey" = '' OR "RequestHash" = '';

                INSERT INTO "PAYMENT" (
                    "Id", "SalesOrderId", "Method", "Status", "Amount", "ConfirmedAmount",
                    "FinalApprovedAt", "FinalApprovalReference", "DeliveredDate", "Version",
                    "CreatedAt", "UpdatedAt")
                SELECT DISTINCT ON (orders."Id")
                    manual."Id", orders."Id", orders."PaymentMethod",
                    CASE WHEN confirmed."Amount" >= orders."FinalAmount" THEN 'Paid' ELSE 'Unpaid' END,
                    orders."FinalAmount", LEAST(orders."FinalAmount", confirmed."Amount"),
                    manual."CreatedAt", 'LEGACY-MANUAL:' || orders."Id"::text,
                    NULL, 0, manual."CreatedAt", manual."UpdatedAt"
                FROM "SALES_ORDER" orders
                JOIN "MANUAL_PAYMENT" manual ON manual."SalesOrderId" = orders."Id"
                JOIN (
                    SELECT "SalesOrderId", COALESCE(SUM("Amount") FILTER (WHERE "Status" = 'Paid'), 0) AS "Amount"
                    FROM "MANUAL_PAYMENT"
                    GROUP BY "SalesOrderId"
                ) confirmed ON confirmed."SalesOrderId" = orders."Id"
                WHERE NOT EXISTS (
                    SELECT 1 FROM "PAYMENT" payment WHERE payment."SalesOrderId" = orders."Id")
                  AND orders."FinalAmount" > 0
                ORDER BY orders."Id", manual."CreatedAt", manual."Id";

                UPDATE "PAYMENT" payment
                SET "ConfirmedAmount" = LEAST(
                        payment."Amount",
                        GREATEST(
                            CASE WHEN payment."Status" = 'Paid' THEN payment."Amount" ELSE 0 END,
                            COALESCE((
                                SELECT SUM(manual."Amount")
                                FROM "MANUAL_PAYMENT" manual
                                WHERE manual."SalesOrderId" = payment."SalesOrderId"
                                  AND manual."Status" = 'Paid'), 0))),
                    "FinalApprovedAt" = payment."CreatedAt",
                    "FinalApprovalReference" = CASE
                        WHEN payment."FinalApprovalReference" = ''
                        THEN 'LEGACY-PAYMENT:' || payment."Id"::text
                        ELSE payment."FinalApprovalReference"
                    END;

                UPDATE "PAYMENT"
                SET "Status" = CASE WHEN "ConfirmedAmount" = "Amount" THEN 'Paid' ELSE 'Unpaid' END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_PayOsWebhookHash",
                table: "PAYMENT",
                column: "PayOsWebhookHash",
                unique: true,
                filter: "\"PayOsWebhookHash\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PAYMENT_Amount",
                table: "PAYMENT",
                sql: "\"Amount\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PAYMENT_ConfirmedAmount",
                table: "PAYMENT",
                sql: "\"ConfirmedAmount\" >= 0 AND \"ConfirmedAmount\" <= \"Amount\"");

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_PAYMENT_OperationKey",
                table: "MANUAL_PAYMENT",
                column: "OperationKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PAYMENT_PayOsWebhookHash",
                table: "PAYMENT");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PAYMENT_Amount",
                table: "PAYMENT");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PAYMENT_ConfirmedAmount",
                table: "PAYMENT");

            migrationBuilder.DropIndex(
                name: "IX_MANUAL_PAYMENT_OperationKey",
                table: "MANUAL_PAYMENT");

            migrationBuilder.DropColumn(
                name: "ConfirmedAmount",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "FinalApprovalReference",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "FinalApprovedAt",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "PayOsWebhookHash",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "OperationKey",
                table: "MANUAL_PAYMENT");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "MANUAL_PAYMENT");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "MANUAL_PAYMENT");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "DeliveredDate",
                table: "PAYMENT",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);
        }
    }
}
