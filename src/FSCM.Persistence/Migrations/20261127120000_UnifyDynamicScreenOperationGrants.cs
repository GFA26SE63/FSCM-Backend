using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FSCM.Persistence.Migrations;

/// <summary>Screen dependencies are independent of business data-scope profiles.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261127120000_UnifyDynamicScreenOperationGrants")]
public sealed class UnifyDynamicScreenOperationGrants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
INSERT INTO "SYS_SCREEN_API_MAPPING" ("BusinessRole","ApiCode","ScreenCode") VALUES
('Admin','api:Allocation.Read','allocation'),
('Admin','api:Allocation.Read','orders'),
('Admin','api:Allocation.Read','warehouse-outbound'),
('Admin','api:Allocation.Read','mobile-wk-picking'),
('Admin','api:TransportDispatches.Read','fleet'),
('Admin','api:InternalTransfers.List','readiness'),
('Admin','api:InternalTransfers.Get','readiness'),
('Admin','api:InternalTransfers.List','readiness'),
('Admin','api:InternalTransfers.Get','readiness')
ON CONFLICT DO NOTHING;

-- Copy the existing per-screen contract to every data-scope profile. Do not add
-- any screen to a role: revoked/unselected screens remain revoked/unselected.
INSERT INTO "SYS_SCREEN_API_MAPPING" ("BusinessRole","ApiCode","ScreenCode")
SELECT profile,dependency."ApiCode",dependency."ScreenCode"
FROM (SELECT DISTINCT "ApiCode","ScreenCode" FROM "SYS_SCREEN_API_MAPPING") dependency
CROSS JOIN unnest(ARRAY['Admin','Operator','WarehouseKeeper','SalesManager','Sales','Retailer']) AS profile
ON CONFLICT DO NOTHING;

-- Backfill only dependencies of screens already selected on each existing role.
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),grants."RoleId",grants."ApiCode",now() FROM (
 SELECT DISTINCT r."Id" AS "RoleId",m."ApiCode"
 FROM "SYS_ROLE" r
 JOIN "SYS_SCREEN_API_MAPPING" m ON m."BusinessRole"=r."BaseRole"
 JOIN "SYS_ROLE_PERMISSION" screen ON screen."RoleId"=r."Id" AND screen."PermissionKey"=m."ScreenCode"
) grants
WHERE NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p
 WHERE p."RoleId"=grants."RoleId" AND p."PermissionKey"=grants."ApiCode");
""");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Dynamic permission dependency migrations are forward-only.");
}
