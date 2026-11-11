using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRetailerDebtPayOsCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RETAILER_DEBT_PAYOS_CHECKOUT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RetailerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PayOsOrderCode = table.Column<long>(type: "bigint", nullable: false),
                    PaymentLinkId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CheckoutUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    QrCode = table.Column<string>(type: "text", nullable: false),
                    PayOsReference = table.Column<string>(type: "character varying(74)", maxLength: 74, nullable: true),
                    WebhookHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OperationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PayOsExpiredAt = table.Column<long>(type: "bigint", nullable: true),
                    PaymentReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RETAILER_DEBT_PAYOS_CHECKOUT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RETAILER_DEBT_PAYOS_CHECKOUT_PAYMENT_RECEIPT_PaymentReceipt~",
                        column: x => x.PaymentReceiptId,
                        principalTable: "PAYMENT_RECEIPT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RETAILER_DEBT_PAYOS_CHECKOUT_RETAILER_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "RETAILER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RETAILER_DEBT_PAYOS_ALLOCATION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RETAILER_DEBT_PAYOS_ALLOCATION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RETAILER_DEBT_PAYOS_ALLOCATION_PAYMENT_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "PAYMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RETAILER_DEBT_PAYOS_ALLOCATION_RETAILER_DEBT_PAYOS_CHECKOUT~",
                        column: x => x.CheckoutId,
                        principalTable: "RETAILER_DEBT_PAYOS_CHECKOUT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_DEBT_PAYOS_ALLOCATION_CheckoutId_PaymentId",
                table: "RETAILER_DEBT_PAYOS_ALLOCATION",
                columns: new[] { "CheckoutId", "PaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_DEBT_PAYOS_ALLOCATION_PaymentId",
                table: "RETAILER_DEBT_PAYOS_ALLOCATION",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_DEBT_PAYOS_CHECKOUT_OperationKey",
                table: "RETAILER_DEBT_PAYOS_CHECKOUT",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_DEBT_PAYOS_CHECKOUT_PaymentReceiptId",
                table: "RETAILER_DEBT_PAYOS_CHECKOUT",
                column: "PaymentReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_DEBT_PAYOS_CHECKOUT_PayOsOrderCode",
                table: "RETAILER_DEBT_PAYOS_CHECKOUT",
                column: "PayOsOrderCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RETAILER_DEBT_PAYOS_CHECKOUT_RetailerId",
                table: "RETAILER_DEBT_PAYOS_CHECKOUT",
                column: "RetailerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RETAILER_DEBT_PAYOS_ALLOCATION");

            migrationBuilder.DropTable(
                name: "RETAILER_DEBT_PAYOS_CHECKOUT");
        }
    }
}
