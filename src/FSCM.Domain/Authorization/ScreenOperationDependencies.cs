namespace FSCM.Domain.Authorization;

/// <summary>Transport-to-screen contract. Unknown business operations fail closed.</summary>
public static class ScreenOperationDependencies
{
    /// <summary>Initial mapping only; role saves and REST authorization read the persisted table.</summary>
    public static IReadOnlyList<FSCM.Domain.Entities.ScreenApiMapping> InitialMappings() =>
        SystemRoles.All.SelectMany(role => ApiPermissionCatalog.All.SelectMany(operation =>
            (Rest(operation.Controller, operation.Action, role) ?? [])
                .Where(screen => !screen.StartsWith("mobile-", StringComparison.Ordinal) || operation.InitialRoles.Contains(role))
                .Select(screen => new FSCM.Domain.Entities.ScreenApiMapping
            { BusinessRole = role, ApiCode = operation.Key, ScreenCode = screen }))).ToArray();

    public static string[]? Rest(string controller, string action, string? role) => controller switch
    {
        "Auth" or "Notifications" => null,
        "Roles" when action == "List" => ["roles", "users"],
        "Roles" => ["roles"],
        "Users" => ["users"],
        // Mobile order screens read only the order step; the controller filters other keys for them.
        "BusinessConfigurations" when action == "List" && role == SystemRoles.Retailer => ["config", "mobile-rt-order-create"],
        "BusinessConfigurations" when action == "List" && role == SystemRoles.Sales => ["config", "mobile-sales-order-create"],
        "BusinessConfigurations" => ["config"],
        "Categories" or "FmcgCategoryCatalog" => Catalog(role, "cats"),
        "Products" => Catalog(role, "skus"),
        "MlorConfigurations" => ["catmlor"],
        "Warehouses" when action is "List" or "Get" => ["whs", "regions", "warehouse-inbound", "warehouse-outbound", "warehouse-transfers"],
        "Warehouses" => ["whs"],
        "Regions" or "SalesGroups" when action is "List" or "Get" => role == SystemRoles.Sales
            ? ["mobile-sales-home", "mobile-sales-declare", "mobile-sales-retailers"] : ["regions", "users", "whs"],
        "Regions" or "SalesGroups" or "TerritoryAssignments" => ["regions"],
        "TerritoryAccess" => ["mobile-sales-order-create"],
        "Dashboard" => ["dashboard"],
        "Reports" => role == SystemRoles.Sales ? ["mobile-sales-kpi"] : ["kpi", "dashboard"],
        "OperationsReports" => ["reports"],
        "WarehouseLogisticsReports" => ["warehouse-report"],
        "Orders" when action is "Create" or "Availability" => role == SystemRoles.Retailer
            ? ["mobile-rt-order-create"] : role == SystemRoles.Sales ? ["mobile-sales-order-create"] : ["orders"],
        "Orders" when action == "SubmitOffline" => ["mobile-sales-sync"],
        "Orders" or "Quotes" => Orders(role),
        "Allocation" => ["allocation", "orders", "warehouse-outbound", "mobile-wk-picking"],
        "Payments" or "PaymentReceipts" => Payments(role),
        "RetailerReceivables" => ["mobile-rt-payments"],
        "Complaints" => role == SystemRoles.Retailer ? ["mobile-rt-complaints"] : ["complaints"],
        "Refunds" => ["refunds"],
        "Retailers" when action is "Declare" or "UpdateDeclaration" or "SubmitForReview" =>
            role == SystemRoles.Sales ? ["mobile-sales-declare"] : ["retailers"],
        "Retailers" => role == SystemRoles.Sales
            ? ["mobile-sales-retailers", "mobile-sales-declarations", "mobile-sales-order-create"] : ["retailers"],
        "Promotions" => role == SystemRoles.Sales ? ["mobile-sales-promos", "mobile-sales-order-create"]
            : role == SystemRoles.Retailer ? ["mobile-rt-order-create"] : ["promos"],
        "Pricing" => role == SystemRoles.Retailer ? ["mobile-rt-order-create"]
            : role == SystemRoles.Sales ? ["mobile-sales-order-create"] : ["orders", "skus"],
        "BatchDiscounts" => ["batchpromo"],
        "ShelfLifeDiscounts" => ["config"],
        "TransitTables" or "Eta" => ["config", "whs"],
        "Loyalty" or "LoyaltyCards" => role == SystemRoles.Retailer ? ["mobile-rt-loyalty"] : ["loyalty"],
        "LoyaltyRewards" => role == SystemRoles.Retailer ? ["mobile-rt-gifts"] : ["gifts", "redeem"],
        "LoyaltyRedemptions" or "LoyaltyRedemptionWorkflow" => role == SystemRoles.Retailer ? ["mobile-rt-gifts"] : ["redeem"],
        "Disposals" or "WriteOffs" => ["disposal"],
        "InternalTransfers" => ["warehouse-transfers"],
        "GoodsReceipts" => ["warehouse-inbound"],
        "Inventory" or "InventoryLocks" => ["batches"],
        "FactoryTraceability" => ["warehouse-trace"],
        "QrLabels" => role == SystemRoles.Sales ? ["mobile-sales-scan"]
            : role == SystemRoles.Retailer ? ["mobile-rt-scan"]
            : role == SystemRoles.WarehouseKeeper ? ["mobile-wk-label", "warehouse-trace", "batches"] : ["batches"],
        "PickingLists" => role == SystemRoles.WarehouseKeeper ? ["warehouse-outbound", "mobile-wk-picking"] : ["warehouse-outbound"],
        "WarehouseDocuments" when action is "FactoryPending" or "FactoryPendingDispatches" or "FactoryReceiptGroups" or "PrepareFactoryDispatch" or "PrepareFactory" or "Factory" or "ReceivePallet" or "MissingPallet" or "ScanPallet" => ["warehouse-inbound", "mobile-wk-operations"],
        "WarehouseDocuments" when action is "IssueWorkflow" or "PrepareCartons" or "RemoveCarton" => ["warehouse-outbound", "mobile-wk-picking", "mobile-wk-operations"],
        "WarehouseDocuments" when action is "Scan" or "Post" or "CreateChild" or "CreateRequest" or "Cancel" => ["warehouse-inbound", "warehouse-factory-requests", "warehouse-outbound", "warehouse-transfers", "mobile-wk-operations", "mobile-wk-picking"],
        "WarehouseDocuments" => role == SystemRoles.WarehouseKeeper
            ? ["warehouse-inbound", "warehouse-factory-requests", "warehouse-outbound", "warehouse-transfers", "mobile-wk-operations", "mobile-wk-picking", "mobile-wk-shipped"]
            : ["warehouse-inbound", "warehouse-factory-requests", "warehouse-outbound", "warehouse-transfers"],
        "Shipments" when action == "ConfirmReceipt" => role == SystemRoles.Retailer ? ["mobile-rt-scan"] : ["orders"],
        "Shipments" when action is "Assign" or "Sync" or "ResolveException" => ["readiness"],
        "Shipments" when action == "Dispatch" => ["readiness", "warehouse-outbound", "mobile-wk-picking", "mobile-wk-operations"],
        "Shipments" when action is "ConfirmIssue" or "Dispatch" or "FlagException" => ["warehouse-outbound", "mobile-wk-picking", "mobile-wk-operations"],
        "Shipments" or "Deliveries" => role == SystemRoles.Retailer ? ["mobile-rt-orders", "mobile-rt-scan"]
            : role == SystemRoles.Sales ? ["mobile-sales-orders"]
            : role == SystemRoles.WarehouseKeeper ? ["warehouse-outbound", "mobile-wk-picking", "mobile-wk-shipped"] : ["readiness", "orders"],
        "TransportDispatches" => ["fleet"],
        "Map" => role == SystemRoles.Sales ? ["mobile-sales-retailers", "mobile-sales-declare"] : ["regions", "whs"],
        "Mobile" => role == SystemRoles.Retailer ? ["mobile-rt-order-create"] : ["mobile-sales-sync"],
        _ => []
    };

    private static string[] Catalog(string? role, string key) => role == SystemRoles.Sales
        ? ["mobile-sales-products", "mobile-sales-order-create", "mobile-sales-sync"]
        : role == SystemRoles.Retailer ? ["mobile-rt-order-create"] : [key];

    private static string[] Orders(string? role) => role == SystemRoles.Sales
        ? ["mobile-sales-orders"] : role == SystemRoles.Retailer ? ["mobile-rt-orders"]
        : role == SystemRoles.WarehouseKeeper ? ["orders", "warehouse-outbound", "mobile-wk-picking"] : ["orders"];

    private static string[] Payments(string? role) => role == SystemRoles.Sales
        ? ["mobile-sales-debts"] : role == SystemRoles.Retailer ? ["mobile-rt-payments"] : ["debts"];
}
