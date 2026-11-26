using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FSCM.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkAccountsToDynamicRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RoleDefinitionId",
                table: "USER_ACCOUNT",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseRole",
                table: "SYS_ROLE",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Operator");

            // Forward-only account links and initial operation grants; existing screen revocations are preserved.
            migrationBuilder.Sql("""
WITH inserted AS (
 INSERT INTO "SYS_ROLE" ("Id","Code","Name","BaseRole","IsSystem","CreatedAt","UpdatedAt")
 SELECT gen_random_uuid(),'Admin','Quản trị viên','Admin',TRUE,now(),now()
 WHERE NOT EXISTS (SELECT 1 FROM "SYS_ROLE" WHERE lower("Code")=lower('Admin')) RETURNING "Id"
)
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),i."Id",p.key,now() FROM inserted i CROSS JOIN unnest(ARRAY['dashboard','reports','warehouse-report','kpi','orders','allocation','debts','refunds','complaints','warehouse-inbound','warehouse-factory-requests','warehouse-outbound','warehouse-transfers','batches','warehouse-trace','readiness','batchpromo','disposal','fleet','retailers','promos','loyalty','redeem','gifts','regions','cats','catmlor','skus','whs','users','roles','config','mobile-sales-home','mobile-sales-orders','mobile-sales-order-create','mobile-sales-scan','mobile-sales-retailers','mobile-sales-declare','mobile-sales-declarations','mobile-sales-products','mobile-sales-promos','mobile-sales-debts','mobile-sales-kpi','mobile-sales-sync','mobile-rt-orders','mobile-rt-order-create','mobile-rt-scan','mobile-rt-payments','mobile-rt-loyalty','mobile-rt-gifts','mobile-rt-complaints','mobile-wk-picking','mobile-wk-operations','mobile-wk-label','mobile-wk-shipped']) AS p(key);
WITH inserted AS (
 INSERT INTO "SYS_ROLE" ("Id","Code","Name","BaseRole","IsSystem","CreatedAt","UpdatedAt")
 SELECT gen_random_uuid(),'Operator','Nhân viên vận hành','Operator',TRUE,now(),now()
 WHERE NOT EXISTS (SELECT 1 FROM "SYS_ROLE" WHERE lower("Code")=lower('Operator')) RETURNING "Id"
)
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),i."Id",p.key,now() FROM inserted i CROSS JOIN unnest(ARRAY['dashboard','reports','warehouse-report','orders','allocation','debts','refunds','complaints','batches','readiness','retailers','loyalty','redeem','kpi','fleet']) AS p(key);
WITH inserted AS (
 INSERT INTO "SYS_ROLE" ("Id","Code","Name","BaseRole","IsSystem","CreatedAt","UpdatedAt")
 SELECT gen_random_uuid(),'WarehouseKeeper','Thủ kho','WarehouseKeeper',TRUE,now(),now()
 WHERE NOT EXISTS (SELECT 1 FROM "SYS_ROLE" WHERE lower("Code")=lower('WarehouseKeeper')) RETURNING "Id"
)
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),i."Id",p.key,now() FROM inserted i CROSS JOIN unnest(ARRAY['dashboard','reports','warehouse-inbound','warehouse-factory-requests','warehouse-outbound','warehouse-transfers','batches','warehouse-trace','disposal','mobile-wk-picking','mobile-wk-operations','mobile-wk-label','mobile-wk-shipped']) AS p(key);
WITH inserted AS (
 INSERT INTO "SYS_ROLE" ("Id","Code","Name","BaseRole","IsSystem","CreatedAt","UpdatedAt")
 SELECT gen_random_uuid(),'SalesManager','Quản lý bán hàng','SalesManager',TRUE,now(),now()
 WHERE NOT EXISTS (SELECT 1 FROM "SYS_ROLE" WHERE lower("Code")=lower('SalesManager')) RETURNING "Id"
)
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),i."Id",p.key,now() FROM inserted i CROSS JOIN unnest(ARRAY['dashboard','reports','regions','kpi']) AS p(key);
WITH inserted AS (
 INSERT INTO "SYS_ROLE" ("Id","Code","Name","BaseRole","IsSystem","CreatedAt","UpdatedAt")
 SELECT gen_random_uuid(),'Sales','Nhân viên bán hàng','Sales',TRUE,now(),now()
 WHERE NOT EXISTS (SELECT 1 FROM "SYS_ROLE" WHERE lower("Code")=lower('Sales')) RETURNING "Id"
)
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),i."Id",p.key,now() FROM inserted i CROSS JOIN unnest(ARRAY['mobile-sales-home','mobile-sales-orders','mobile-sales-order-create','mobile-sales-scan','mobile-sales-retailers','mobile-sales-declare','mobile-sales-declarations','mobile-sales-products','mobile-sales-promos','mobile-sales-debts','mobile-sales-kpi','mobile-sales-sync']) AS p(key);
WITH inserted AS (
 INSERT INTO "SYS_ROLE" ("Id","Code","Name","BaseRole","IsSystem","CreatedAt","UpdatedAt")
 SELECT gen_random_uuid(),'Retailer','Nhà bán lẻ','Retailer',TRUE,now(),now()
 WHERE NOT EXISTS (SELECT 1 FROM "SYS_ROLE" WHERE lower("Code")=lower('Retailer')) RETURNING "Id"
)
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),i."Id",p.key,now() FROM inserted i CROSS JOIN unnest(ARRAY['mobile-rt-orders','mobile-rt-order-create','mobile-rt-scan','mobile-rt-payments','mobile-rt-loyalty','mobile-rt-gifts','mobile-rt-complaints']) AS p(key);
UPDATE "SYS_ROLE" SET "BaseRole" = CASE lower("Code")
 WHEN 'admin' THEN 'Admin' WHEN 'operator' THEN 'Operator' WHEN 'warehousekeeper' THEN 'WarehouseKeeper'
 WHEN 'salesmanager' THEN 'SalesManager' WHEN 'sales' THEN 'Sales' WHEN 'retailer' THEN 'Retailer' ELSE 'Operator' END;
INSERT INTO "SYS_ROLE" ("Id","Code","Name","BaseRole","IsSystem","CreatedAt","UpdatedAt")
SELECT gen_random_uuid(),u."Role",u."Role",'Operator',FALSE,now(),now()
FROM (SELECT DISTINCT "Role" FROM "USER_ACCOUNT") u
WHERE NOT EXISTS (SELECT 1 FROM "SYS_ROLE" r WHERE lower(r."Code")=lower(u."Role"));
UPDATE "USER_ACCOUNT" u SET "RoleDefinitionId"=r."Id", "Role"=r."BaseRole"
FROM "SYS_ROLE" r WHERE lower(u."Role")=lower(r."Code");
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:BatchDiscounts.Suggestions',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:BatchDiscounts.Suggestions');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:BatchDiscounts.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:BatchDiscounts.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:BatchDiscounts.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:BatchDiscounts.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:BatchDiscounts.Apply',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:BatchDiscounts.Apply');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:BatchDiscounts.Remove',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:BatchDiscounts.Remove');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:BusinessConfigurations.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:BusinessConfigurations.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:BusinessConfigurations.History',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:BusinessConfigurations.History');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:BusinessConfigurations.UpdateBatch',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:BusinessConfigurations.UpdateBatch');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:BusinessConfigurations.Update',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:BusinessConfigurations.Update');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Categories.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Categories.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Categories.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Categories.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Categories.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Categories.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Categories.Update',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Categories.Update');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Categories.UpdateStatus',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Categories.UpdateStatus');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Categories.SetDefaultMlor',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Categories.SetDefaultMlor');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Categories.Delete',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Categories.Delete');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Complaints.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Complaints.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Complaints.Targets',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Complaints.Targets');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Complaints.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Complaints.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Complaints.Resolve',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Complaints.Resolve');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Complaints.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Complaints.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Complaints.RequestEvidence',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Complaints.RequestEvidence');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Complaints.Reject',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Complaints.Reject');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Complaints.Approve',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Complaints.Approve');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Complaints.CompleteHandling',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Complaints.CompleteHandling');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Complaints.Cancel',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Complaints.Cancel');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Complaints.AddEvidence',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Complaints.AddEvidence');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Complaints.RemoveEvidence',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Complaints.RemoveEvidence');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Complaints.ListEvidence',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Complaints.ListEvidence');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Dashboard.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Dashboard.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Deliveries.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Deliveries.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Deliveries.GetByOrder',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Deliveries.GetByOrder');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Deliveries.Drivers',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Deliveries.Drivers');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Deliveries.Vehicles',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Deliveries.Vehicles');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Deliveries.CreateDriver',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Deliveries.CreateDriver');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Deliveries.UpdateDriver',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Deliveries.UpdateDriver');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Deliveries.SetDriverStatus',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Deliveries.SetDriverStatus');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Deliveries.CreateVehicle',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Deliveries.CreateVehicle');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Deliveries.UpdateVehicle',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Deliveries.UpdateVehicle');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Deliveries.SetVehicleStatus',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Deliveries.SetVehicleStatus');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Disposals.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Disposals.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Disposals.Candidates',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Disposals.Candidates');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Disposals.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Disposals.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Disposals.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Disposals.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Eta.Schedule',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Eta.Schedule');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Eta.Estimate',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Eta.Estimate');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:FactoryTraceability.Application',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:FactoryTraceability.Application');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:FactoryTraceability.Trace',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:FactoryTraceability.Trace');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:FmcgCategoryCatalog.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:FmcgCategoryCatalog.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:FmcgCategoryCatalog.History',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:FmcgCategoryCatalog.History');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:FmcgCategoryCatalog.Put',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:FmcgCategoryCatalog.Put');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:FmcgCategoryCatalog.Evaluate',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:FmcgCategoryCatalog.Evaluate');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:GoodsReceipts.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:GoodsReceipts.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:GoodsReceipts.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:GoodsReceipts.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:GoodsReceipts.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:GoodsReceipts.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:GoodsReceipts.Approve',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:GoodsReceipts.Approve');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:InternalTransfers.Suggestions',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:InternalTransfers.Suggestions');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:InternalTransfers.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:InternalTransfers.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:InternalTransfers.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:InternalTransfers.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:InternalTransfers.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:InternalTransfers.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:InternalTransfers.Approve',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:InternalTransfers.Approve');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:InternalTransfers.Reject',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:InternalTransfers.Reject');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:InternalTransfers.Dispatch',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:InternalTransfers.Dispatch');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:InternalTransfers.Receive',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:InternalTransfers.Receive');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Inventory.QrCodes',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Inventory.QrCodes');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Inventory.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Inventory.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Inventory.PalletCount',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Inventory.PalletCount');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Inventory.GetBatch',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Inventory.GetBatch');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Inventory.History',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Inventory.History');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Inventory.ExpiryHealth',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Inventory.ExpiryHealth');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:InventoryLocks.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:InventoryLocks.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:InventoryLocks.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:InventoryLocks.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:InventoryLocks.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:InventoryLocks.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:InventoryLocks.Release',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:InventoryLocks.Release');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyCards.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyCards.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyCards.Me',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyCards.Me');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyCards.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyCards.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyCards.GetByCode',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyCards.GetByCode');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Loyalty.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Loyalty.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Loyalty.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Loyalty.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Loyalty.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Loyalty.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Loyalty.Update',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Loyalty.Update');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Loyalty.Activate',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Loyalty.Activate');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Loyalty.Deactivate',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Loyalty.Deactivate');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyRedemptions.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyRedemptions.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyRedemptions.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyRedemptions.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyRedemptionWorkflow.Approve',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyRedemptionWorkflow.Approve');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyRedemptionWorkflow.Reject',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyRedemptionWorkflow.Reject');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyRedemptionWorkflow.Deliver',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyRedemptionWorkflow.Deliver');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyRedemptionWorkflow.Complete',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyRedemptionWorkflow.Complete');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyRewards.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyRewards.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyRewards.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyRewards.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyRewards.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyRewards.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyRewards.Update',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyRewards.Update');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyRewards.SetImage',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyRewards.SetImage');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyRewards.Activate',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyRewards.Activate');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:LoyaltyRewards.Deactivate',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:LoyaltyRewards.Deactivate');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Map.Geocode',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Map.Geocode');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Map.GetLocation',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Map.GetLocation');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Map.RefreshDistance',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Map.RefreshDistance');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Map.ListDistances',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Map.ListDistances');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:MlorConfigurations.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:MlorConfigurations.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:MlorConfigurations.Set',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:MlorConfigurations.Set');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:MlorConfigurations.Deactivate',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:MlorConfigurations.Deactivate');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Mobile.Snapshot',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Retailer','Sales')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Mobile.Snapshot');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:OperationsReports.Read',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:OperationsReports.Read');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Orders.Availability',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Orders.Availability');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Orders.RetryAllocation',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Orders.RetryAllocation');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Orders.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Orders.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Orders.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Orders.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Orders.GetByCode',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Orders.GetByCode');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Orders.Timeline',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Orders.Timeline');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Orders.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Retailer','Sales')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Orders.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Orders.SubmitOffline',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Sales')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Orders.SubmitOffline');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Orders.Cancel',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Sales')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Orders.Cancel');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:PaymentReceipts.Confirm',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:PaymentReceipts.Confirm');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:PaymentReceipts.ApplyCredit',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:PaymentReceipts.ApplyCredit');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:PaymentReceipts.Reverse',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:PaymentReceipts.Reverse');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:PaymentReceipts.ProposeAllocation',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:PaymentReceipts.ProposeAllocation');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Payments.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Payments.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Payments.GetByOrder',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Payments.GetByOrder');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Payments.Collect',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Sales')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Payments.Collect');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Payments.Adjust',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Payments.Adjust');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Payments.SwitchToCredit',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Payments.SwitchToCredit');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Payments.CreatePayOsCheckout',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Payments.CreatePayOsCheckout');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:PickingLists.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:PickingLists.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:PickingLists.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:PickingLists.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:PickingLists.Scan',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:PickingLists.Scan');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:PickingLists.Complete',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:PickingLists.Complete');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Pricing.Snapshot',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Pricing.Snapshot');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Pricing.CartSnapshot',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Pricing.CartSnapshot');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Products.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Products.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Products.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Products.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Products.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Products.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Products.Update',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Products.Update');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Products.Delete',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Products.Delete');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Products.Publish',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Products.Publish');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Products.Inactivate',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Products.Inactivate');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Products.CreateReplacement',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Products.CreateReplacement');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Products.Clone',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Products.Clone');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Products.SetImage',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Products.SetImage');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Products.ListPrices',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Products.ListPrices');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Products.GetPriceSuggestion',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Products.GetPriceSuggestion');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Products.SetPrice',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Products.SetPrice');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Promotions.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Promotions.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Promotions.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Promotions.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Promotions.CreateCampaignGroup',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Promotions.CreateCampaignGroup');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Promotions.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Promotions.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Promotions.Update',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Promotions.Update');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Promotions.Activate',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Promotions.Activate');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Promotions.Deactivate',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Promotions.Deactivate');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:QrLabels.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:QrLabels.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:QrLabels.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:QrLabels.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:QrLabels.Print',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:QrLabels.Print');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:QrLabels.GenerationCapacity',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:QrLabels.GenerationCapacity');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:QrLabels.PrintBatch',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:QrLabels.PrintBatch');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:QrLabels.Generate',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:QrLabels.Generate');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:QrLabels.Split',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:QrLabels.Split');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:QrLabels.Scan',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:QrLabels.Scan');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Quotes.ForOrder',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Quotes.ForOrder');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Quotes.ForRun',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Quotes.ForRun');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Quotes.Accept',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Quotes.Accept');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Quotes.Reject',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Quotes.Reject');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Refunds.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Refunds.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Refunds.Obligations',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Refunds.Obligations');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Refunds.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Refunds.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Refunds.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Refunds.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Refunds.Evidence',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Refunds.Evidence');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Refunds.Verify',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Refunds.Verify');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Refunds.UploadEvidenceFile',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Refunds.UploadEvidenceFile');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Refunds.EvidenceUrl',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Refunds.EvidenceUrl');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Regions.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Regions.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Regions.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Regions.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Regions.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Regions.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Regions.Update',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Regions.Update');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Regions.UpdateStatus',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Regions.UpdateStatus');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Reports.SalesRanking',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Sales','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Reports.SalesRanking');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Reports.SalesKpis',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Sales','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Reports.SalesKpis');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Reports.BestSellingProducts',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Sales','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Reports.BestSellingProducts');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:RetailerReceivables.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:RetailerReceivables.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:RetailerReceivables.GetByOrder',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:RetailerReceivables.GetByOrder');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:RetailerReceivables.CreatePayOsCheckout',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:RetailerReceivables.CreatePayOsCheckout');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:RetailerReceivables.GetPayOsCheckout',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:RetailerReceivables.GetPayOsCheckout');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Retailers.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Sales','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Retailers.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Retailers.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Sales','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Retailers.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Retailers.Declare',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Sales')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Retailers.Declare');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Retailers.UpdateDeclaration',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Sales')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Retailers.UpdateDeclaration');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Retailers.UpdateProfile',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Retailers.UpdateProfile');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Retailers.SubmitForReview',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Sales')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Retailers.SubmitForReview');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Retailers.Review',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Retailers.Review');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Retailers.UpdateCreditTerms',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Retailers.UpdateCreditTerms');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Retailers.UpdateStatus',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Retailers.UpdateStatus');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Retailers.SetStorefrontImage',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Retailers.SetStorefrontImage');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Retailers.CheckOrderEligibility',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Sales')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Retailers.CheckOrderEligibility');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Retailers.ResolveMlor',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Sales','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Retailers.ResolveMlor');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Roles.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Roles.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Roles.GetScreens',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Roles.GetScreens');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Roles.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Roles.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Roles.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Roles.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Roles.Update',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Roles.Update');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Roles.Delete',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Roles.Delete');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:SalesGroups.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:SalesGroups.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:SalesGroups.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:SalesGroups.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:SalesGroups.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:SalesGroups.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:SalesGroups.Update',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:SalesGroups.Update');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:SalesGroups.UpdateStatus',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:SalesGroups.UpdateStatus');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:ShelfLifeDiscounts.Current',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:ShelfLifeDiscounts.Current');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:ShelfLifeDiscounts.History',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:ShelfLifeDiscounts.History');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:ShelfLifeDiscounts.Version',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:ShelfLifeDiscounts.Version');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:ShelfLifeDiscounts.Match',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:ShelfLifeDiscounts.Match');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:ShelfLifeDiscounts.Publish',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:ShelfLifeDiscounts.Publish');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Shipments.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Shipments.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Shipments.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Shipments.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Shipments.OrderReadiness',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Shipments.OrderReadiness');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Shipments.ConfirmIssue',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Shipments.ConfirmIssue');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Shipments.OrderDeliveryView',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Shipments.OrderDeliveryView');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Shipments.Sync',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Shipments.Sync');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Shipments.FlagException',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Shipments.FlagException');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Shipments.ResolveException',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Shipments.ResolveException');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Shipments.Assign',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Shipments.Assign');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Shipments.Dispatch',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Shipments.Dispatch');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Shipments.ConfirmReceipt',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Retailer')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Shipments.ConfirmReceipt');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:TerritoryAccess.CheckOrderEligibility',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Sales')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:TerritoryAccess.CheckOrderEligibility');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:TerritoryAssignments.ListSales',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:TerritoryAssignments.ListSales');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:TerritoryAssignments.TransferSales',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:TerritoryAssignments.TransferSales');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:TerritoryAssignments.ListRetailers',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:TerritoryAssignments.ListRetailers');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:TerritoryAssignments.AssignRetailer',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','SalesManager')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:TerritoryAssignments.AssignRetailer');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:TransitTables.Current',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:TransitTables.Current');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:TransitTables.History',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:TransitTables.History');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:TransitTables.Version',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:TransitTables.Version');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:TransitTables.Lookup',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:TransitTables.Lookup');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:TransitTables.Publish',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:TransitTables.Publish');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:TransportDispatches.AssignTransfer',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:TransportDispatches.AssignTransfer');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Users.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Users.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Users.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Users.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Users.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Users.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Users.UpdateProfile',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Users.UpdateProfile');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Users.GetScope',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Users.GetScope');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Users.UpdateRole',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Users.UpdateRole');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Users.UpdateStatus',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Users.UpdateStatus');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Users.AssignWarehouse',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Users.AssignWarehouse');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Users.UnassignWarehouse',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Users.UnassignWarehouse');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Users.AssignSalesGroup',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Users.AssignSalesGroup');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Users.UnassignSalesGroup',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Users.UnassignSalesGroup');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Users.AssignRegion',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Users.AssignRegion');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Users.UnassignRegion',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Users.UnassignRegion');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Users.AssignRetailer',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Users.AssignRetailer');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Users.UnassignRetailer',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Users.UnassignRetailer');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.IssueWorkflow',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.IssueWorkflow');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.FactoryPending',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.FactoryPending');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.FactoryReceiptGroups',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.FactoryReceiptGroups');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.FactoryPendingDispatches',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.FactoryPendingDispatches');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.PrepareFactoryDispatch',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.PrepareFactoryDispatch');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.ExpectedPallets',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.ExpectedPallets');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.PrepareFactory',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.PrepareFactory');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.CreateRequest',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.CreateRequest');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.CreateChild',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.CreateChild');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.Factory',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.Factory');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.PreviewPallet',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.PreviewPallet');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.ReceiptReports',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.ReceiptReports');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.ScanPallet',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.ScanPallet');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.PalletScans',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.PalletScans');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.ReceiptPrint',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.ReceiptPrint');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.IssuePrint',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.IssuePrint');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.MissingPallet',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.MissingPallet');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.ReceivePallet',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.ReceivePallet');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.RemoveCarton',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.RemoveCarton');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.PrepareCartons',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.PrepareCartons');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.StockLabels',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.StockLabels');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.Cancel',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.Cancel');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.Scan',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.Scan');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.Post',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.Post');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseDocuments.Labels',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseDocuments.Labels');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WarehouseLogisticsReports.Read',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WarehouseLogisticsReports.Read');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Warehouses.List',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Warehouses.List');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Warehouses.Get',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','Retailer','Sales','SalesManager','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Warehouses.Get');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Warehouses.Create',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Warehouses.Create');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Warehouses.Update',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Warehouses.Update');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:Warehouses.UpdateStatus',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:Warehouses.UpdateStatus');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WriteOffs.AddAttachment',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WriteOffs.AddAttachment');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WriteOffs.ListAttachments',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator','WarehouseKeeper')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WriteOffs.ListAttachments');
INSERT INTO "SYS_ROLE_PERMISSION" ("Id","RoleId","PermissionKey","CreatedAt")
SELECT gen_random_uuid(),r."Id",'api:WriteOffs.RemoveAttachment',now() FROM "SYS_ROLE" r
WHERE r."BaseRole" IN ('Admin','Operator')
AND NOT EXISTS (SELECT 1 FROM "SYS_ROLE_PERMISSION" p WHERE p."RoleId"=r."Id" AND p."PermissionKey"='api:WriteOffs.RemoveAttachment');
CREATE FUNCTION fscm_link_account_role() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE assigned "SYS_ROLE"%ROWTYPE;
BEGIN
 IF NEW."RoleDefinitionId" IS NULL THEN
  SELECT * INTO assigned FROM "SYS_ROLE" WHERE lower("Code")=lower(NEW."Role");
 ELSE
  SELECT * INTO assigned FROM "SYS_ROLE" WHERE "Id"=NEW."RoleDefinitionId";
 END IF;
 IF assigned."Id" IS NULL THEN RAISE EXCEPTION 'Account must reference an existing role' USING ERRCODE='23503'; END IF;
 NEW."RoleDefinitionId" := assigned."Id";
 NEW."Role" := assigned."BaseRole";
 RETURN NEW;
END; $$;
CREATE TRIGGER tr_fscm_account_role BEFORE INSERT OR UPDATE OF "RoleDefinitionId", "Role" ON "USER_ACCOUNT"
FOR EACH ROW EXECUTE FUNCTION fscm_link_account_role();
""");

            migrationBuilder.CreateIndex(
                name: "IX_USER_ACCOUNT_RoleDefinitionId",
                table: "USER_ACCOUNT",
                column: "RoleDefinitionId");

            migrationBuilder.AddForeignKey(
                name: "FK_USER_ACCOUNT_SYS_ROLE_RoleDefinitionId",
                table: "USER_ACCOUNT",
                column: "RoleDefinitionId",
                principalTable: "SYS_ROLE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Account-role links are forward-only; deploy a corrective migration.");
    }
}
