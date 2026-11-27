using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FSCM.Persistence.Migrations;

/// <summary>The transport dispatch screen owns starting its assigned warehouse trips.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261127100000_MapDeliveryDispatchScreen")]
public partial class MapDeliveryDispatchScreen : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
INSERT INTO "SYS_SCREEN_API_MAPPING" ("BusinessRole","ApiCode","ScreenCode") VALUES
('Admin','api:Shipments.Dispatch','readiness'),
('Operator','api:Shipments.Dispatch','readiness')
ON CONFLICT DO NOTHING;
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Shipments.Dispatch',now()
FROM "SYS_ROLE" r
JOIN "SYS_ROLE_PERMISSION" s ON s."RoleId"=r."Id" AND s."PermissionKey"='readiness'
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p
    WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Shipments.Dispatch');
""");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Dispatch permission mappings are forward-only.");
}
