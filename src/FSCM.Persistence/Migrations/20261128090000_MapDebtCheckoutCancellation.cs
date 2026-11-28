using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FSCM.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128090000_MapDebtCheckoutCancellation")]
public sealed class MapDebtCheckoutCancellation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
INSERT INTO "SYS_SCREEN_API_MAPPING" ("BusinessRole","ApiCode","ScreenCode")
SELECT profile,operation,'mobile-rt-payments'
FROM unnest(ARRAY['Admin','Retailer']) profile
CROSS JOIN unnest(ARRAY['api:RetailerReceivables.ListPendingPayOsCheckouts','api:RetailerReceivables.CancelPayOsCheckout']) operation
ON CONFLICT DO NOTHING;

INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",m."ApiCode",now()
FROM "SYS_ROLE" r
JOIN "SYS_SCREEN_API_MAPPING" m ON m."BusinessRole"=r."BaseRole"
WHERE m."ApiCode" IN ('api:RetailerReceivables.ListPendingPayOsCheckouts','api:RetailerReceivables.CancelPayOsCheckout')
 AND EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" screen
             WHERE screen."RoleId"=r."Id" AND screen."PermissionKey"=m."ScreenCode")
 AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" api
                 WHERE api."RoleId"=r."Id" AND api."PermissionKey"=m."ApiCode")
GROUP BY r."Id",m."ApiCode";
""");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Dynamic permission dependencies are forward-only.");
}
