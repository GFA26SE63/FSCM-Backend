using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryLock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "INVENTORY_LOCK",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RequiresResolution = table.Column<bool>(type: "boolean", nullable: false),
                    LockedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReleasedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INVENTORY_LOCK", x => x.Id);
                    table.CheckConstraint("CK_INVENTORY_LOCK_LockedQuantity", "\"LockedQuantity\" > 0 AND TRUNC(\"LockedQuantity\") = \"LockedQuantity\"");
                    table.ForeignKey(
                        name: "FK_INVENTORY_LOCK_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_INVENTORY_LOCK_PRODUCT_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PRODUCT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_INVENTORY_LOCK_USER_ACCOUNT_LockedByUserId",
                        column: x => x.LockedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_INVENTORY_LOCK_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_LOCK_BatchId",
                table: "INVENTORY_LOCK",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_LOCK_BatchId_Status",
                table: "INVENTORY_LOCK",
                columns: new[] { "BatchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_LOCK_LockedByUserId",
                table: "INVENTORY_LOCK",
                column: "LockedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_LOCK_ProductId",
                table: "INVENTORY_LOCK",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_LOCK_WarehouseId_Status",
                table: "INVENTORY_LOCK",
                columns: new[] { "WarehouseId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "INVENTORY_LOCK");
        }
    }
}
