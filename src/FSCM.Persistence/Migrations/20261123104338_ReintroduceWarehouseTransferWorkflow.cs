using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReintroduceWarehouseTransferWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WAREHOUSE_TRANSFER",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SourceWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationWarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverId = table.Column<Guid>(type: "uuid", nullable: true),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverCodeSnapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DriverNameSnapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    PlateNumberSnapshot = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_TRANSFER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_TRANSFER_DRIVER_DriverId",
                        column: x => x.DriverId,
                        principalTable: "DRIVER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                        name: "FK_WAREHOUSE_TRANSFER_VEHICLE_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "VEHICLE",
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
                name: "WAREHOUSE_TRANSFER_ITEM",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseTransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    DispatchedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                name: "IX_WAREHOUSE_TRANSFER_DriverId_Status",
                table: "WAREHOUSE_TRANSFER",
                columns: new[] { "DriverId", "Status" });

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
                name: "IX_WAREHOUSE_TRANSFER_VehicleId_Status",
                table: "WAREHOUSE_TRANSFER",
                columns: new[] { "VehicleId", "Status" });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WAREHOUSE_TRANSFER_EVENT");

            migrationBuilder.DropTable(
                name: "WAREHOUSE_TRANSFER_LABEL");

            migrationBuilder.DropTable(
                name: "WAREHOUSE_TRANSFER_ITEM");

            migrationBuilder.DropTable(
                name: "WAREHOUSE_TRANSFER");
        }
    }
}
