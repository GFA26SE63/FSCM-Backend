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
                // Notifications are an authenticated private inbox, not a delegated business screen.
                var operation = EntityReadOperations.For(method.Name, resource);
                if (principal?.Identity?.IsAuthenticated != true || (resource != "Notifications" &&
                    (operation is null || !await context.Service<OperationAccess>().AllowsAsync(principal,
                        operation.Value.Controller, operation.Value.Action, context.RequestAborted))))
                    throw new GraphQLException(ErrorBuilder.New().SetMessage("Access denied.")
                        .SetCode("AUTH_NOT_AUTHORIZED").Build());
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
        "UserReads" or "UserRead" or "UserScopeBatchReads" or "UserAccounts" or "UserWarehouseAssignments" or "UserRegionAssignments" or "AuditLogs" => "Users",
        "RoleDefinitions" or "RolePermissions" or "RoleReads" or "RoleRead" or "ScreenFeatures" => "Roles",
        "RedemptionReads" or "RewardRedemptions" or "RewardRedemptionTotals" => "LoyaltyRedemptions",
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
        "DeliveryDispatchPickingReads" or "DeliveryPickingReads" or "PickingLists" or "PickingItems" or "RunPickingLists" or "RunPickingGathers" or "RunPickingSplits" => "PickingLists",
        "Payments" or "PaymentReceipts" or "PaymentReceiptAllocations" => "Payments",
        "RetailerDebtPayOsCheckouts" or "RetailerDebtPayOsAllocations" => "RetailerReceivables",
        "Deliveries" or "Shipments" or "ShipmentReadinessRead" or "ShipmentReadinessBatchRead" or "BusyTransportRead" => "Shipments",
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

/// <summary>Explicit read contracts; never infer permission from a mutable method-name prefix.</summary>
public static class EntityReadOperations
{
    public static (string Controller, string Action)? For(string field, string resource)
    {
        var special = field switch
        {
            "RoleRead" => ("Roles", "Get"),
            "ScreenFeatures" => ("Roles", "GetScreens"),
            "UserRead" or "UserScopeBatchReads" => ("Users", "Get"),
            "OrderRead" => ("Orders", "Get"),
            "WarehouseProfile" => ("Warehouses", "Get"),
            "RetailerProfile" => ("Retailers", "Get"),
            "RetailerMlorResolution" => ("Retailers", "ResolveMlor"),
            "PricingSnapshot" => ("Pricing", "Snapshot"),
            "ShipmentReadinessRead" or "ShipmentReadinessBatchRead" => ("Shipments", "OrderReadiness"),
            "BusyTransportRead" => ("Shipments", "Assign"),
            "Drivers" or "Vehicles" => ("TransportDispatches", "Read"),
            _ => (resource, resource switch
            {
                "Allocation" => "Read",
                "TransitTables" or "ShelfLifeDiscounts" => "Current",
                "Map" => "GetLocation",
                _ => "List"
            })
        };
        return FSCM.Domain.Authorization.ApiPermissionCatalog.All.Any(p =>
            p.Controller == special.Item1 && p.Action == special.Item2) ? special : null;
    }
}
