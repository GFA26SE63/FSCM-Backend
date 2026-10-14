using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInternalTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "WAREHOUSE_TRANSFER",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_TRANSFER_EVENT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseTransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                name: "WAREHOUSE_TRANSFER_LABEL",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseTransferItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchQrLabelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
                name: "IX_WAREHOUSE_TRANSFER_LABEL_BatchQrLabelId",
                table: "WAREHOUSE_TRANSFER_LABEL",
                column: "BatchQrLabelId");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_TRANSFER_LABEL_WarehouseTransferItemId_BatchQrLab~",
                table: "WAREHOUSE_TRANSFER_LABEL",
                columns: new[] { "WarehouseTransferItemId", "BatchQrLabelId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WAREHOUSE_TRANSFER_EVENT");

            migrationBuilder.DropTable(
                name: "WAREHOUSE_TRANSFER_LABEL");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "WAREHOUSE_TRANSFER");
        }
    }
}
