using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FSCM.Persistence.Migrations;

/// <summary>Mobile order screens read the order step from GET /configurations (filtered to that key).</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261127090000_MapMobileOrderStepConfiguration")]
public partial class MapMobileOrderStepConfiguration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
INSERT INTO "SYS_SCREEN_API_MAPPING" ("BusinessRole","ApiCode","ScreenCode") VALUES
('Retailer','api:BusinessConfigurations.List','mobile-rt-order-create'),
('Sales','api:BusinessConfigurations.List','mobile-sales-order-create')
ON CONFLICT DO NOTHING;
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),grants."RoleId",'api:BusinessConfigurations.List',now() FROM (
 SELECT DISTINCT r."Id" AS "RoleId"
 FROM "SYS_ROLE" r
 JOIN "SYS_SCREEN_API_MAPPING" m ON m."BusinessRole"=r."BaseRole" AND m."ApiCode"='api:BusinessConfigurations.List'
 JOIN "SYS_ROLE_PERMISSION" s ON s."RoleId"=r."Id" AND s."PermissionKey"=m."ScreenCode"
 WHERE NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p
   WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:BusinessConfigurations.List')
) grants;
""");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Permission mappings are forward-only.");
}
