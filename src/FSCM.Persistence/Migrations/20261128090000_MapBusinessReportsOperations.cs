using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FSCM.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128090000_MapBusinessReportsOperations")]
public sealed class MapBusinessReportsOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
DELETE FROM "SYS_ROLE_PERMISSION"
WHERE "PermissionKey" = 'api:OperationsReports.Read';

DELETE FROM "SYS_SCREEN_API_MAPPING"
WHERE "ApiCode" = 'api:OperationsReports.Read';

DELETE FROM "SYS_SCREEN_API_MAPPING"
WHERE "ScreenCode" = 'mobile-sales-debts'
  AND "ApiCode" = 'api:Payments.Collect';

DELETE FROM "SYS_ROLE_PERMISSION" p
USING "SYS_ROLE" r
WHERE p."RoleId" = r."Id"
  AND r."BaseRole" = 'Sales'
  AND r."IsSystem" = true
  AND p."PermissionKey" = 'api:Payments.Collect';

INSERT INTO "SYS_SCREEN_API_MAPPING" ("BusinessRole","ApiCode","ScreenCode")
SELECT profile, operation, 'reports'
FROM unnest(ARRAY['Admin','Operator','WarehouseKeeper','SalesManager','Sales','Retailer']) profile
CROSS JOIN unnest(ARRAY[
    'api:BusinessReports.Financial',
    'api:BusinessReports.Receivables',
    'api:BusinessReports.Promotions',
    'api:BusinessReports.Loyalty',
    'api:BusinessReports.Claims',
    'api:BusinessReports.Fulfillment',
    'api:BusinessReports.InventoryRisk'
]) operation
ON CONFLICT DO NOTHING;

INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(), r."Id", grants."ApiCode", now()
FROM "SYS_ROLE" r
JOIN (
    VALUES
        ('Admin','api:BusinessReports.Financial'),
        ('Admin','api:BusinessReports.Receivables'),
        ('Admin','api:BusinessReports.Promotions'),
        ('Admin','api:BusinessReports.Loyalty'),
        ('Admin','api:BusinessReports.Claims'),
        ('Admin','api:BusinessReports.Fulfillment'),
        ('Admin','api:BusinessReports.InventoryRisk'),
        ('Operator','api:BusinessReports.Financial'),
        ('Operator','api:BusinessReports.Receivables'),
        ('Operator','api:BusinessReports.Promotions'),
        ('Operator','api:BusinessReports.Loyalty'),
        ('Operator','api:BusinessReports.Claims'),
        ('Operator','api:BusinessReports.Fulfillment'),
        ('Operator','api:BusinessReports.InventoryRisk'),
        ('SalesManager','api:BusinessReports.Financial'),
        ('SalesManager','api:BusinessReports.Receivables'),
        ('SalesManager','api:BusinessReports.Promotions'),
        ('SalesManager','api:BusinessReports.Loyalty'),
        ('SalesManager','api:BusinessReports.Claims'),
        ('SalesManager','api:BusinessReports.Fulfillment'),
        ('WarehouseKeeper','api:BusinessReports.Fulfillment'),
        ('WarehouseKeeper','api:BusinessReports.InventoryRisk')
) AS grants("BaseRole","ApiCode") ON grants."BaseRole" = r."BaseRole"
WHERE EXISTS (
    SELECT 1 FROM "SYS_ROLE_PERMISSION" screen
    WHERE screen."RoleId" = r."Id" AND screen."PermissionKey" = 'reports'
)
AND NOT EXISTS (
    SELECT 1 FROM "SYS_ROLE_PERMISSION" existing
    WHERE existing."RoleId" = r."Id" AND existing."PermissionKey" = grants."ApiCode"
);
""");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Dynamic permission dependency migrations are forward-only.");
}
