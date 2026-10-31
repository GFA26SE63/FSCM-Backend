using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeReview2PaymentReceiptsAndWarehouseRegions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RegionId",
                table: "WAREHOUSE",
                type: "uuid",
                nullable: true);

            // Legacy warehouses had no geographic FK. A deployment must already have a Region;
            // map unclassified historical warehouses to its first stable code before enforcing
            // the new mandatory relation. No synthetic warehouse tier/region is created here.
            migrationBuilder.Sql("""
                DO $$
                DECLARE fallback_region uuid;
                BEGIN
                    SELECT "Id" INTO fallback_region FROM "REGION" ORDER BY "Code" LIMIT 1;
                    IF fallback_region IS NULL THEN
                        RAISE EXCEPTION 'Cannot migrate WAREHOUSE.RegionId: REGION is empty.';
                    END IF;
                    UPDATE "WAREHOUSE" SET "RegionId" = fallback_region WHERE "RegionId" IS NULL;
                END $$;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "RegionId",
                table: "WAREHOUSE",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "Type",
                table: "WAREHOUSE");

            migrationBuilder.CreateTable(
                name: "PAYMENT_RECEIPT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RetailerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UnappliedCreditAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversalReason = table.Column<string>(type: "text", nullable: true),
                    OperationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYMENT_RECEIPT", x => x.Id);
                    table.CheckConstraint("CK_PAYMENT_RECEIPT_ReceivedAmount", "\"ReceivedAmount\" > 0");
                    table.CheckConstraint("CK_PAYMENT_RECEIPT_Status", "\"Status\" IN (0, 1, 2)");
                    table.CheckConstraint("CK_PAYMENT_RECEIPT_UnappliedCreditAmount", "\"UnappliedCreditAmount\" >= 0 AND \"UnappliedCreditAmount\" <= \"ReceivedAmount\"");
                    table.ForeignKey(
                        name: "FK_PAYMENT_RECEIPT_RETAILER_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "RETAILER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PAYMENT_RECEIPT_USER_ACCOUNT_ConfirmedByUserId",
                        column: x => x.ConfirmedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PAYMENT_RECEIPT_USER_ACCOUNT_ReversedByUserId",
                        column: x => x.ReversedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PAYMENT_RECEIPT_ALLOCATION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYMENT_RECEIPT_ALLOCATION", x => x.Id);
                    table.CheckConstraint("CK_PAYMENT_RECEIPT_ALLOCATION_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_PAYMENT_RECEIPT_ALLOCATION_PAYMENT_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "PAYMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PAYMENT_RECEIPT_ALLOCATION_PAYMENT_RECEIPT_PaymentReceiptId",
                        column: x => x.PaymentReceiptId,
                        principalTable: "PAYMENT_RECEIPT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_RegionId",
                table: "WAREHOUSE",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_RECEIPT_ConfirmedByUserId",
                table: "PAYMENT_RECEIPT",
                column: "ConfirmedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_RECEIPT_OperationKey",
                table: "PAYMENT_RECEIPT",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_RECEIPT_ReceiptCode",
                table: "PAYMENT_RECEIPT",
                column: "ReceiptCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_RECEIPT_RetailerId_Status",
                table: "PAYMENT_RECEIPT",
                columns: new[] { "RetailerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_RECEIPT_ReversedByUserId",
                table: "PAYMENT_RECEIPT",
                column: "ReversedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_RECEIPT_ALLOCATION_PaymentId",
                table: "PAYMENT_RECEIPT_ALLOCATION",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_RECEIPT_ALLOCATION_PaymentReceiptId_PaymentId",
                table: "PAYMENT_RECEIPT_ALLOCATION",
                columns: new[] { "PaymentReceiptId", "PaymentId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WAREHOUSE_REGION_RegionId",
                table: "WAREHOUSE",
                column: "RegionId",
                principalTable: "REGION",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WAREHOUSE_REGION_RegionId",
                table: "WAREHOUSE");

            migrationBuilder.DropTable(
                name: "PAYMENT_RECEIPT_ALLOCATION");

            migrationBuilder.DropTable(
                name: "PAYMENT_RECEIPT");

            migrationBuilder.DropIndex(
                name: "IX_WAREHOUSE_RegionId",
                table: "WAREHOUSE");

            migrationBuilder.DropColumn(
                name: "RegionId",
                table: "WAREHOUSE");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "WAREHOUSE",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }
    }
}
