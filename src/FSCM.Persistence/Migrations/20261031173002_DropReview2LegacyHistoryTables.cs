using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropReview2LegacyHistoryTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS "HISTORY_ALLOCATION_TRACE_CANDIDATE" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_ALLOCATION_LINE_TRACE" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_ALLOCATION_PLAN_PRICE" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_ALLOCATION_PLAN_VERSION" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_ORPHAN_ALLOCATION_RUN" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_PICKING_ITEM_WITHOUT_PO" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_PICKING_LIST_WITHOUT_PO" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_RUN_PICKING_SPLIT" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_RUN_PICKING_GATHER" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_RUN_PICKING_LIST" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_WAREHOUSE_TRANSFER_LABEL" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_WAREHOUSE_TRANSFER_EVENT" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_WAREHOUSE_TRANSFER_ITEM" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_WAREHOUSE_TRANSFER" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_STOCK_HOLD" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_MANUAL_PAYMENT" CASCADE;
                DROP TABLE IF EXISTS "HISTORY_BATCH_PROMOTION" CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Length > 0)
                throw new NotSupportedException(
                    "Forward-only cleanup: dropped legacy test tables and their data cannot be restored.");
        }
    }
}
