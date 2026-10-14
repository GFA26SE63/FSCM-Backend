using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQrLabelLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "QrLabelCodeSequence",
                startValue: 1000L);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentWarehouseId",
                table: "BATCH_QR_LABEL",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentLabelId",
                table: "BATCH_QR_LABEL",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RootLabelId",
                table: "BATCH_QR_LABEL",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "BATCH_QR_LABEL",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "BATCH_QR_LABEL",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql("""
                UPDATE "BATCH_QR_LABEL" AS label
                SET "CurrentWarehouseId" = batch."WarehouseId",
                    "RootLabelId" = label."Id",
                    "UpdatedAt" = label."GeneratedAt",
                    "Version" = CASE WHEN label."Version" < 1 THEN 1 ELSE label."Version" END
                FROM "BATCH" AS batch
                WHERE label."BatchId" = batch."Id";
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "CurrentWarehouseId",
                table: "BATCH_QR_LABEL",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "BATCH_QR_LABEL",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "QR_LABEL_EVENT",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchQrLabelId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReferenceCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QR_LABEL_EVENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QR_LABEL_EVENT_BATCH_QR_LABEL_BatchQrLabelId",
                        column: x => x.BatchQrLabelId,
                        principalTable: "BATCH_QR_LABEL",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QR_LABEL_EVENT_USER_ACCOUNT_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "USER_ACCOUNT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QR_LABEL_EVENT_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_QR_LABEL_CurrentWarehouseId",
                table: "BATCH_QR_LABEL",
                column: "CurrentWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_QR_LABEL_ParentLabelId",
                table: "BATCH_QR_LABEL",
                column: "ParentLabelId");

            migrationBuilder.CreateIndex(
                name: "IX_BATCH_QR_LABEL_RootLabelId",
                table: "BATCH_QR_LABEL",
                column: "RootLabelId");

            migrationBuilder.CreateIndex(
                name: "IX_QR_LABEL_EVENT_BatchQrLabelId_OccurredAt",
                table: "QR_LABEL_EVENT",
                columns: new[] { "BatchQrLabelId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QR_LABEL_EVENT_OperationKey",
                table: "QR_LABEL_EVENT",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QR_LABEL_EVENT_PerformedByUserId",
                table: "QR_LABEL_EVENT",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QR_LABEL_EVENT_WarehouseId",
                table: "QR_LABEL_EVENT",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_BATCH_QR_LABEL_BATCH_QR_LABEL_ParentLabelId",
                table: "BATCH_QR_LABEL",
                column: "ParentLabelId",
                principalTable: "BATCH_QR_LABEL",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BATCH_QR_LABEL_BATCH_QR_LABEL_RootLabelId",
                table: "BATCH_QR_LABEL",
                column: "RootLabelId",
                principalTable: "BATCH_QR_LABEL",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BATCH_QR_LABEL_WAREHOUSE_CurrentWarehouseId",
                table: "BATCH_QR_LABEL",
                column: "CurrentWarehouseId",
                principalTable: "WAREHOUSE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BATCH_QR_LABEL_BATCH_QR_LABEL_ParentLabelId",
                table: "BATCH_QR_LABEL");

            migrationBuilder.DropForeignKey(
                name: "FK_BATCH_QR_LABEL_BATCH_QR_LABEL_RootLabelId",
                table: "BATCH_QR_LABEL");

            migrationBuilder.DropForeignKey(
                name: "FK_BATCH_QR_LABEL_WAREHOUSE_CurrentWarehouseId",
                table: "BATCH_QR_LABEL");

            migrationBuilder.DropTable(
                name: "QR_LABEL_EVENT");

            migrationBuilder.DropIndex(
                name: "IX_BATCH_QR_LABEL_CurrentWarehouseId",
                table: "BATCH_QR_LABEL");

            migrationBuilder.DropIndex(
                name: "IX_BATCH_QR_LABEL_ParentLabelId",
                table: "BATCH_QR_LABEL");

            migrationBuilder.DropIndex(
                name: "IX_BATCH_QR_LABEL_RootLabelId",
                table: "BATCH_QR_LABEL");

            migrationBuilder.DropColumn(
                name: "CurrentWarehouseId",
                table: "BATCH_QR_LABEL");

            migrationBuilder.DropColumn(
                name: "ParentLabelId",
                table: "BATCH_QR_LABEL");

            migrationBuilder.DropColumn(
                name: "RootLabelId",
                table: "BATCH_QR_LABEL");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "BATCH_QR_LABEL");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "BATCH_QR_LABEL");

            migrationBuilder.DropSequence(
                name: "QrLabelCodeSequence");
        }
    }
}
