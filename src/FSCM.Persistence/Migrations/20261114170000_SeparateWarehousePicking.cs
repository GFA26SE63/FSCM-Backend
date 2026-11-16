using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FSCM.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261114170000_SeparateWarehousePicking")]
public sealed class SeparateWarehousePicking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE "WAREHOUSE_DOCUMENT"
        SET "Kind" = 'Picking', "Version" = "Version" + 1
        WHERE "Kind" = 'Issue' AND "Status" IN ('Open', 'Prepared');
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM "WAREHOUSE_DOCUMENT" child
                JOIN "WAREHOUSE_DOCUMENT" parent ON child."SourceDocumentId" = parent."Id"
                WHERE child."Kind" = 'Issue' AND parent."Kind" = 'Picking') THEN
                RAISE EXCEPTION 'Cannot collapse posted picking/issue audit documents';
            END IF;
        END $$;
        UPDATE "WAREHOUSE_DOCUMENT" SET "Kind" = 'Issue', "Version" = "Version" + 1
        WHERE "Kind" = 'Picking';
        """);
}
