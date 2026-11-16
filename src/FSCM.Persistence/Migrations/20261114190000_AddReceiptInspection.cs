using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace FSCM.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261114190000_AddReceiptInspection")]
public sealed class AddReceiptInspection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "ReceiptInspectionJson", table: "FACTORY_PALLET", type: "text", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ReceiptInspectionJson", table: "FACTORY_RECEIPT_SYNC", type: "text", nullable: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM "FACTORY_PALLET" WHERE "ReceiptInspectionJson" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Cannot discard recorded receipt inspection evidence';
                END IF;
            END $$;
            """);
        migrationBuilder.DropColumn(name: "ReceiptInspectionJson", table: "FACTORY_PALLET");
        migrationBuilder.DropColumn(name: "ReceiptInspectionJson", table: "FACTORY_RECEIPT_SYNC");
    }
}
