using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntegratePayOsPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayOsCheckoutUrl",
                table: "PAYMENT",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PayOsExpiredAt",
                table: "PAYMENT",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayOsLinkStatus",
                table: "PAYMENT",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PayOsOrderCode",
                table: "PAYMENT",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayOsPaymentLinkId",
                table: "PAYMENT",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayOsQrCode",
                table: "PAYMENT",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayOsReference",
                table: "PAYMENT",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_PayOsOrderCode",
                table: "PAYMENT",
                column: "PayOsOrderCode",
                unique: true,
                filter: "\"PayOsOrderCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_PayOsPaymentLinkId",
                table: "PAYMENT",
                column: "PayOsPaymentLinkId",
                unique: true,
                filter: "\"PayOsPaymentLinkId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_PayOsReference",
                table: "PAYMENT",
                column: "PayOsReference",
                unique: true,
                filter: "\"PayOsReference\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PAYMENT_PayOsOrderCode",
                table: "PAYMENT");

            migrationBuilder.DropIndex(
                name: "IX_PAYMENT_PayOsPaymentLinkId",
                table: "PAYMENT");

            migrationBuilder.DropIndex(
                name: "IX_PAYMENT_PayOsReference",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "PayOsCheckoutUrl",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "PayOsExpiredAt",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "PayOsLinkStatus",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "PayOsOrderCode",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "PayOsPaymentLinkId",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "PayOsQrCode",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "PayOsReference",
                table: "PAYMENT");
        }
    }
}
