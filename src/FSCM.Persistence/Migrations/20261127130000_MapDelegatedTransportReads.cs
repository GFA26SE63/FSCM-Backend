using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FSCM.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261127130000_MapDelegatedTransportReads")]
public sealed class MapDelegatedTransportReads : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
INSERT INTO "SYS_SCREEN_API_MAPPING" ("BusinessRole","ApiCode","ScreenCode")
SELECT profile,operation,screen
FROM unnest(ARRAY['Admin','Operator','WarehouseKeeper','SalesManager','Sales','Retailer']) profile
CROSS JOIN unnest(ARRAY['api:TransportDispatches.Read','api:TransportDispatches.AssignTransfer']) operation
CROSS JOIN unnest(ARRAY['fleet','readiness','warehouse-transfers']) screen
ON CONFLICT DO NOTHING;

INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",m."ApiCode",now()
FROM "SYS_ROLE" r
JOIN "SYS_SCREEN_API_MAPPING" m ON m."BusinessRole"=r."BaseRole"
WHERE m."ApiCode" IN ('api:TransportDispatches.Read','api:TransportDispatches.AssignTransfer')
 AND EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" screen
             WHERE screen."RoleId"=r."Id" AND screen."PermissionKey"=m."ScreenCode")
 AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" api
                 WHERE api."RoleId"=r."Id" AND api."PermissionKey"=m."ApiCode")
GROUP BY r."Id",m."ApiCode";
""");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Dynamic permission dependencies are forward-only.");
}
