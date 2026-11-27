using FSCM.API.Security;
using HotChocolate.Types;
using Microsoft.AspNetCore.Authorization;

namespace FSCM.API.GraphQL;

/// <summary>Apply grants before resolving each selected field, including aliased/batched queries.</summary>
public sealed class EntityQueryType : ObjectType<EntityQuery>
{
    protected override void Configure(IObjectTypeDescriptor<EntityQuery> descriptor)
    {
        foreach (var method in typeof(EntityQuery).GetMethods(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                     System.Reflection.BindingFlags.DeclaredOnly))
        {
            var resource = EntityScreenResources.For(method.Name);
            descriptor.Field(method).Use(next => async context =>
            {
                var principal = context.Service<IHttpContextAccessor>().HttpContext?.User;
                var role = principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
                var keys = ScreenPermissionMap.Rest(resource, "List", role);
                if (keys is not null)
                {
                    var authorization = context.Service<IAuthorizationService>();
                    var allowed = false;
                    foreach (var key in keys)
                    {
                        if (principal is not null && (await authorization.AuthorizeAsync(principal, null, "permission:" + key)).Succeeded)
                        {
                            allowed = true;
                            break;
                        }
                    }
                    if (!allowed)
                        throw new GraphQLException(ErrorBuilder.New().SetMessage("Access denied.")
                            .SetCode("AUTH_NOT_AUTHORIZED").Build());
                }
                await next(context);
            });
        }
    }
}

public static class EntityScreenResources
{
    public static string For(string field) => field switch
    {
        "ComplaintReads" or "ReceiptDiscrepancies" or "ReceiptDiscrepancyItems" or "ReplacementCases" or "ReturnHandlingDocuments" or "ComplaintEvidences" => "Complaints",
        "RefundReads" or "ManualRefunds" => "Refunds",
        "GoodsReceiptReads" or "GoodsReceipts" or "GoodsReceiptLines" => "GoodsReceipts",
        "UserReads" or "UserRead" or "UserAccounts" or "UserWarehouseAssignments" or "UserRegionAssignments" or "AuditLogs" => "Users",
        "RoleDefinitions" or "RolePermissions" or "RoleReads" or "RoleRead" or "ScreenFeatures" => "Roles",
        "RedemptionReads" or "RewardRedemptions" => "LoyaltyRedemptions",
        "DisposalReads" or "WriteOffs" or "WriteOffItems" or "WriteOffAttachments" => "Disposals",
        "PromotionReads" or "Promotions" or "PromotionProducts" or "PromotionCategories" or "PromotionRetailers" => "Promotions",
        "OrderReads" or "OrderRead" or "SalesOrders" or "SalesOrderLines" or "OrderPromotions" or "OrderStatusHistories" or "SalesOrderAttachments" or "SalesOrderQuotes" or "PurchaseOrders" or "ReceiptConfirmations" or "ReceiptConfirmationItems" or "ReceiptConfirmationQrLabels" => "Orders",
        "PricingSnapshot" => "Pricing",
        "WarehouseProfiles" or "WarehouseProfile" or "Warehouses" => "Warehouses",
        "RetailerProfiles" or "RetailerProfile" or "RetailerMlorResolution" or "Retailers" or "RetailerSalesAssignments" or "RetailerProductMlors" or "RetailerDeclarationContacts" => "Retailers",
        "Regions" or "SalesGroups" or "SalesGroupMembers" => "Regions",
        "ProductCategories" => "Categories",
        "Products" or "ProductPrices" => "Products",
        "TransitTables" or "TransitTableBands" or "TransitTimeRules" => "TransitTables",
        "ShelfLifeDiscountTables" or "ShelfLifeDiscountTiers" => "ShelfLifeDiscounts",
        "Batches" or "InventoryBalances" or "InventoryTransactions" or "ExpirySweepRuns" => "Inventory",
        "BatchQrLabels" or "QrLabelEvents" => "QrLabels",
        "PickingLists" or "PickingItems" or "RunPickingLists" or "RunPickingGathers" or "RunPickingSplits" => "PickingLists",
        "Payments" or "PaymentReceipts" or "PaymentReceiptAllocations" => "Payments",
        "RetailerDebtPayOsCheckouts" or "RetailerDebtPayOsAllocations" => "RetailerReceivables",
        "Deliveries" or "Shipments" or "ShipmentReadinessRead" or "BusyTransportRead" => "Shipments",
        "Drivers" or "Vehicles" => "TransportDispatches",
        "Notifications" => "Notifications",
        "RetailerLoyalties" or "LoyaltyTransactions" or "LoyaltyPrograms" or "LoyaltyProgramRetailers" => "Loyalty",
        "LoyaltyRewards" => "LoyaltyRewards",
        "MapLocations" or "MapDistanceCaches" => "Map",
        "SystemConfigurations" => "BusinessConfigurations",
        "WarehouseDocuments" or "WarehouseDocumentLines" or "WarehouseDocumentCartons" => "WarehouseDocuments",
        "BatchPromotions" => "BatchDiscounts",
        "WarehouseTransfers" or "WarehouseTransferItems" or "WarehouseTransferEvents" or "WarehouseTransferLabels" => "InternalTransfers",
        "Allocations" or "Reservations" or "StockHolds" or "AllocationRuns" or "AllocationRunOrders" or "AllocationRunLines" or "AllocationRunStocks" or "AllocationRunAllocations" or "AllocationPlanPrices" or "AllocationPlanVersions" or "AllocationLineTraces" or "AllocationTraceCandidates" or "AllocationTraceScores" => "Allocation",
        _ => "Unmapped"
    };
}
