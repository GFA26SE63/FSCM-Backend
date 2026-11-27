using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FSCM.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261127110000_AllowWarehouseKeeperDeliveryDispatch")]
public sealed class AllowWarehouseKeeperDeliveryDispatch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
-- These commands remain operator-owned; saving a keeper role must not derive them
-- from its newly available delivery screen.
DELETE FROM "SYS_SCREEN_API_MAPPING"
WHERE "BusinessRole"='WarehouseKeeper' AND "ScreenCode"='readiness'
AND ("ApiCode" IN ('api:Shipments.Assign','api:Shipments.Sync','api:Shipments.ResolveException')
 OR "ApiCode" LIKE 'api:Deliveries.%');

INSERT INTO "SYS_SCREEN_API_MAPPING" ("BusinessRole","ApiCode","ScreenCode")
SELECT 'WarehouseKeeper',api,'readiness' FROM unnest(ARRAY[
 'api:Shipments.Dispatch','api:Shipments.List','api:Shipments.Get',
 'api:Shipments.OrderReadiness','api:Shipments.OrderDeliveryView',
 'api:PickingLists.List','api:PickingLists.Get','api:Orders.List','api:Warehouses.List','api:Warehouses.Get'
]) AS api ON CONFLICT DO NOTHING;

-- Existing outbound keepers can open the delivery screen. Restricted custom roles
-- without outbound/readiness access receive no new screen or command permission.
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'readiness',now() FROM "SYS_ROLE" r
WHERE r."BaseRole"='WarehouseKeeper'
AND EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='warehouse-outbound')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='readiness');

INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",m."ApiCode",now()
FROM "SYS_ROLE" r
JOIN "SYS_SCREEN_API_MAPPING" m ON m."BusinessRole"=r."BaseRole" AND m."ScreenCode"='readiness'
WHERE r."BaseRole"='WarehouseKeeper'
AND m."ApiCode" IN ('api:Shipments.Dispatch','api:Shipments.List','api:Shipments.Get',
 'api:Shipments.OrderReadiness','api:Shipments.OrderDeliveryView',
 'api:PickingLists.List','api:PickingLists.Get','api:Orders.List','api:Warehouses.List','api:Warehouses.Get')
AND EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='readiness')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"=m."ApiCode");
""");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Warehouse dispatch permissions are forward-only.");
}
