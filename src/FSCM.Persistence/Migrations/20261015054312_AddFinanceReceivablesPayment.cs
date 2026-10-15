using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceReceivablesPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PAYMENT_SalesOrderId",
                table: "PAYMENT");

            migrationBuilder.AddColumn<string>(
                name: "CollectionMethod",
                table: "PAYMENT",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DeliveredDate",
                table: "PAYMENT",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "PAYMENT",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationKey",
                table: "PAYMENT",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PaidDate",
                table: "PAYMENT",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "PAYMENT",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "PAYMENT",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE "PAYMENT" AS payment
                SET "DeliveredDate" = (delivery."DeliveredAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date,
                    "DueDate" = CASE
                        WHEN payment."Method" = 'Credit'
                        THEN (delivery."DeliveredAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date + orders."CreditTermDays"
                        ELSE (delivery."DeliveredAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date
                    END,
                    "PaidDate" = CASE
                        WHEN payment."Status" = 'Paid'
                        THEN (COALESCE(payment."ConfirmedAt", delivery."DeliveredAt") AT TIME ZONE 'Asia/Ho_Chi_Minh')::date
                        ELSE NULL
                    END
                FROM "DELIVERY" AS delivery, "SALES_ORDER" AS orders
                WHERE payment."SalesOrderId" = delivery."SalesOrderId"
                  AND orders."Id" = payment."SalesOrderId"
                  AND delivery."DeliveredAt" IS NOT NULL;

                INSERT INTO "PAYMENT" (
                    "Id", "SalesOrderId", "Method", "Status", "Amount",
                    "DeliveredDate", "DueDate", "PaidDate", "CollectionMethod",
                    "OperationKey", "RequestHash", "ConfirmedByUserId", "ConfirmedAt",
                    "Note", "Version", "CreatedAt", "UpdatedAt")
                SELECT
                    orders."Id", orders."Id", orders."PaymentMethod",
                    CASE WHEN orders."PaymentMethod" = 'Credit' THEN 'Unpaid' ELSE 'Paid' END,
                    orders."FinalAmount",
                    (delivery."DeliveredAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date,
                    CASE WHEN orders."PaymentMethod" = 'Credit'
                        THEN (delivery."DeliveredAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date + orders."CreditTermDays"
                        ELSE (delivery."DeliveredAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date END,
                    CASE WHEN orders."PaymentMethod" = 'Credit' THEN NULL
                        ELSE (delivery."DeliveredAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date END,
                    CASE WHEN orders."PaymentMethod" = 'Credit' THEN NULL ELSE 'DeliverySettlement' END,
                    NULL, NULL,
                    CASE WHEN orders."PaymentMethod" = 'Credit' THEN NULL ELSE receipt."ConfirmedByRetailerUserId" END,
                    CASE WHEN orders."PaymentMethod" = 'Credit' THEN NULL ELSE delivery."DeliveredAt" END,
                    CASE WHEN orders."PaymentMethod" = 'Credit'
                        THEN 'Khoản phải thu được tạo khi nâng cấp dữ liệu Finance'
                        ELSE 'Tự động tất toán từ dữ liệu giao hàng hiện có' END,
                    0, delivery."DeliveredAt", delivery."DeliveredAt"
                FROM "SALES_ORDER" AS orders
                JOIN "DELIVERY" AS delivery ON delivery."SalesOrderId" = orders."Id"
                LEFT JOIN "RECEIPT_CONFIRMATION" AS receipt ON receipt."SalesOrderId" = orders."Id"
                WHERE orders."Status" = 'Delivered'
                  AND delivery."DeliveredAt" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM "PAYMENT" AS existing
                      WHERE existing."SalesOrderId" = orders."Id");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_OperationKey",
                table: "PAYMENT",
                column: "OperationKey",
                unique: true,
                filter: "\"OperationKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_SalesOrderId",
                table: "PAYMENT",
                column: "SalesOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_Status_DueDate",
                table: "PAYMENT",
                columns: new[] { "Status", "DueDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PAYMENT_OperationKey",
                table: "PAYMENT");

            migrationBuilder.DropIndex(
                name: "IX_PAYMENT_SalesOrderId",
                table: "PAYMENT");

            migrationBuilder.DropIndex(
                name: "IX_PAYMENT_Status_DueDate",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "CollectionMethod",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "DeliveredDate",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "OperationKey",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "PaidDate",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "PAYMENT");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_SalesOrderId",
                table: "PAYMENT",
                column: "SalesOrderId");
        }
    }
}
