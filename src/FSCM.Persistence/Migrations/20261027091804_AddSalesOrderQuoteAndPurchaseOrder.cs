using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderQuoteAndPurchaseOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PURCHASE_ORDER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderQuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IssuedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_ORDER", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SALES_ORDER_QUOTE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationPlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_ORDER_QUOTE", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_ORDER_PurchaseOrderCode",
                table: "PURCHASE_ORDER",
                column: "PurchaseOrderCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_ORDER_SalesOrderQuoteId",
                table: "PURCHASE_ORDER",
                column: "SalesOrderQuoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_QUOTE_AllocationPlanVersionId_Status",
                table: "SALES_ORDER_QUOTE",
                columns: new[] { "AllocationPlanVersionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SALES_ORDER_QUOTE_SalesOrderId_RevisionNumber",
                table: "SALES_ORDER_QUOTE",
                columns: new[] { "SalesOrderId", "RevisionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_ORDER");

            migrationBuilder.DropTable(
                name: "SALES_ORDER_QUOTE");
        }
    }
}
