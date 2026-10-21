using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManualPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MANUAL_PAYMENT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_PAYMENT_ConfirmedByUserId",
                table: "MANUAL_PAYMENT",
                column: "ConfirmedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_PAYMENT_SalesOrderId",
                table: "MANUAL_PAYMENT",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MANUAL_PAYMENT_SalesOrderId_Status",
                table: "MANUAL_PAYMENT",
                columns: new[] { "SalesOrderId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MANUAL_PAYMENT");
        }
    }
}
