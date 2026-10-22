using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsReceiptLineProductionDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The physical database may already contain this column (created by an earlier
            // schema revision). Add it only if missing so the migration is safe to apply
            // against both fresh and pre-existing databases.
            migrationBuilder.Sql(
                "ALTER TABLE \"GOODS_RECEIPT_LINE\" ADD COLUMN IF NOT EXISTS \"ProductionDate\" date NOT NULL DEFAULT DATE '0001-01-01';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"GOODS_RECEIPT_LINE\" DROP COLUMN IF EXISTS \"ProductionDate\";");
        }
    }
}
