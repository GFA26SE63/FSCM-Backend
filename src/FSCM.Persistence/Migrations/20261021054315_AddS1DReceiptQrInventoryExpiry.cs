using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddS1DReceiptQrInventoryExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NOTIFICATION_UserAccountId",
                table: "NOTIFICATION");

            migrationBuilder.DropCheckConstraint(
                name: "CK_INVENTORY_BALANCE_AvailableQuantity",
                table: "INVENTORY_BALANCE");

            migrationBuilder.DropCheckConstraint(
                name: "CK_INVENTORY_BALANCE_HeldQuantity",
                table: "INVENTORY_BALANCE");

            migrationBuilder.Sql("""
                UPDATE "INVENTORY_BALANCE"
                SET "AvailableQuantity" = "PhysicalQuantity" - "ReservedQuantity";
                """);

            migrationBuilder.DropColumn(
                name: "HeldQuantityAfter",
                table: "INVENTORY_TRANSACTION");

            migrationBuilder.DropColumn(
                name: "HeldQuantityDelta",
                table: "INVENTORY_TRANSACTION");

            migrationBuilder.DropColumn(
                name: "HeldQuantity",
                table: "INVENTORY_BALANCE");

            migrationBuilder.AddColumn<Guid>(
                name: "OutboxMessageId",
                table: "NOTIFICATION",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                table: "INVENTORY_TRANSACTION",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "INVENTORY_TRANSACTION",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "INVENTORY_TRANSACTION"
                SET "SourceType" = CASE
                        WHEN "GoodsReceiptId" IS NOT NULL THEN 'GoodsReceipt'
                        ELSE 'LegacyInventoryTransaction'
                    END,
                    "SourceId" = COALESCE("GoodsReceiptId", "BatchId");
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceId",
                table: "INVENTORY_TRANSACTION",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SourceType",
                table: "INVENTORY_TRANSACTION",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "EXPIRY_SWEEP_RUN",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    BatchesScanned = table.Column<int>(type: "integer", nullable: false),
                    StatusesUpdated = table.Column<int>(type: "integer", nullable: false),
                    BatchesExpired = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EXPIRY_SWEEP_RUN", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_OutboxMessageId",
                table: "NOTIFICATION",
                column: "OutboxMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_UserAccountId_OutboxMessageId",
                table: "NOTIFICATION",
                columns: new[] { "UserAccountId", "OutboxMessageId" },
                unique: true,
                filter: "\"OutboxMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_TRANSACTION_SourceType_SourceId",
                table: "INVENTORY_TRANSACTION",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_INVENTORY_BALANCE_AvailableQuantity",
                table: "INVENTORY_BALANCE",
                sql: "\"AvailableQuantity\" = \"PhysicalQuantity\" - \"ReservedQuantity\" AND \"AvailableQuantity\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_EXPIRY_SWEEP_RUN_StartedAt",
                table: "EXPIRY_SWEEP_RUN",
                column: "StartedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_NOTIFICATION_OUTBOX_MESSAGE_OutboxMessageId",
                table: "NOTIFICATION",
                column: "OutboxMessageId",
                principalTable: "OUTBOX_MESSAGE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NOTIFICATION_OUTBOX_MESSAGE_OutboxMessageId",
                table: "NOTIFICATION");

            migrationBuilder.DropTable(
                name: "EXPIRY_SWEEP_RUN");

            migrationBuilder.DropIndex(
                name: "IX_NOTIFICATION_OutboxMessageId",
                table: "NOTIFICATION");

            migrationBuilder.DropIndex(
                name: "IX_NOTIFICATION_UserAccountId_OutboxMessageId",
                table: "NOTIFICATION");

            migrationBuilder.DropIndex(
                name: "IX_INVENTORY_TRANSACTION_SourceType_SourceId",
                table: "INVENTORY_TRANSACTION");

            migrationBuilder.DropCheckConstraint(
                name: "CK_INVENTORY_BALANCE_AvailableQuantity",
                table: "INVENTORY_BALANCE");

            migrationBuilder.DropColumn(
                name: "OutboxMessageId",
                table: "NOTIFICATION");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "INVENTORY_TRANSACTION");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "INVENTORY_TRANSACTION");

            migrationBuilder.AddColumn<decimal>(
                name: "HeldQuantityAfter",
                table: "INVENTORY_TRANSACTION",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "HeldQuantityDelta",
                table: "INVENTORY_TRANSACTION",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "HeldQuantity",
                table: "INVENTORY_BALANCE",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_UserAccountId",
                table: "NOTIFICATION",
                column: "UserAccountId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_INVENTORY_BALANCE_AvailableQuantity",
                table: "INVENTORY_BALANCE",
                sql: "\"AvailableQuantity\" = \"PhysicalQuantity\" - \"HeldQuantity\" - \"ReservedQuantity\" AND \"AvailableQuantity\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_INVENTORY_BALANCE_HeldQuantity",
                table: "INVENTORY_BALANCE",
                sql: "\"HeldQuantity\" >= 0");
        }
    }
}
