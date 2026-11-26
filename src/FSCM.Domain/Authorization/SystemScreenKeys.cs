namespace FSCM.Domain.Authorization;

public static class SystemScreenKeys
{
    // Overview
    public const string Dashboard = "dashboard";
    public const string Reports = "reports";
    public const string WarehouseReport = "warehouse-report";
    public const string Kpi = "kpi";

    // Sales & Finance
    public const string Orders = "orders";
    public const string Allocation = "allocation";
    public const string Debts = "debts";
    public const string Refunds = "refunds";
    public const string Complaints = "complaints";

    // Warehouse & Logistics
    public const string WarehouseInbound = "warehouse-inbound";
    public const string WarehouseFactoryRequests = "warehouse-factory-requests";
    public const string WarehouseOutbound = "warehouse-outbound";
    public const string WarehouseTransfers = "warehouse-transfers";
    public const string Batches = "batches";
    public const string WarehouseTrace = "warehouse-trace";
    public const string Readiness = "readiness";
    public const string BatchPromo = "batchpromo";
    public const string Disposal = "disposal";
    public const string Fleet = "fleet";

    // Customer & Loyalty
    public const string Retailers = "retailers";
    public const string Promos = "promos";
    public const string Loyalty = "loyalty";
    public const string Redeem = "redeem";
    public const string Gifts = "gifts";

    // Catalog & Master Data
    public const string Regions = "regions";
    public const string Cats = "cats";
    public const string CatMlor = "catmlor";
    public const string Skus = "skus";
    public const string Whs = "whs";

    // System Administration
    public const string Users = "users";
    public const string Roles = "roles";
    public const string Config = "config";

    public static readonly IReadOnlyList<string> WebScreens = new[]
    {
        Dashboard, Reports, WarehouseReport, Kpi,
        Orders, Allocation, Debts, Refunds, Complaints,
        WarehouseInbound, WarehouseFactoryRequests, WarehouseOutbound, WarehouseTransfers, Batches, WarehouseTrace, Readiness, BatchPromo, Disposal, Fleet,
        Retailers, Promos, Loyalty, Redeem, Gifts,
        Regions, Cats, CatMlor, Skus, Whs,
        Users, Roles, Config
    };

    // Mobile App - Sales
    public const string MobileSalesHome = "mobile-sales-home";
    public const string MobileSalesOrders = "mobile-sales-orders";
    public const string MobileSalesOrderCreate = "mobile-sales-order-create";
    public const string MobileSalesScan = "mobile-sales-scan";
    public const string MobileSalesRetailers = "mobile-sales-retailers";
    public const string MobileSalesDeclare = "mobile-sales-declare";
    public const string MobileSalesDeclarations = "mobile-sales-declarations";
    public const string MobileSalesProducts = "mobile-sales-products";
    public const string MobileSalesPromos = "mobile-sales-promos";
    public const string MobileSalesDebts = "mobile-sales-debts";
    public const string MobileSalesKpi = "mobile-sales-kpi";
    public const string MobileSalesSync = "mobile-sales-sync";

    public static readonly IReadOnlyList<string> MobileSalesScreens = new[]
    {
        MobileSalesHome, MobileSalesOrders, MobileSalesOrderCreate, MobileSalesScan,
        MobileSalesRetailers, MobileSalesDeclare, MobileSalesDeclarations, MobileSalesProducts,
        MobileSalesPromos, MobileSalesDebts, MobileSalesKpi, MobileSalesSync
    };

    // Mobile App - Retailer
    public const string MobileRtOrders = "mobile-rt-orders";
    public const string MobileRtOrderCreate = "mobile-rt-order-create";
    public const string MobileRtScan = "mobile-rt-scan";
    public const string MobileRtPayments = "mobile-rt-payments";
    public const string MobileRtLoyalty = "mobile-rt-loyalty";
    public const string MobileRtGifts = "mobile-rt-gifts";
    public const string MobileRtComplaints = "mobile-rt-complaints";

    public static readonly IReadOnlyList<string> MobileRetailerScreens = new[]
    {
        MobileRtOrders, MobileRtOrderCreate, MobileRtScan, MobileRtPayments,
        MobileRtLoyalty, MobileRtGifts, MobileRtComplaints
    };

    // Mobile App - Warehouse
    public const string MobileWkPicking = "mobile-wk-picking";
    public const string MobileWkOperations = "mobile-wk-operations";
    public const string MobileWkLabel = "mobile-wk-label";
    public const string MobileWkShipped = "mobile-wk-shipped";

    public static readonly IReadOnlyList<string> MobileWarehouseScreens = new[]
    {
        MobileWkPicking, MobileWkOperations, MobileWkLabel, MobileWkShipped
    };

    public static readonly IReadOnlyList<string> MobileScreens = new[]
    {
        MobileSalesHome, MobileSalesOrders, MobileSalesOrderCreate, MobileSalesScan,
        MobileSalesRetailers, MobileSalesDeclare, MobileSalesDeclarations, MobileSalesProducts,
        MobileSalesPromos, MobileSalesDebts, MobileSalesKpi, MobileSalesSync,
        MobileRtOrders, MobileRtOrderCreate, MobileRtScan, MobileRtPayments,
        MobileRtLoyalty, MobileRtGifts, MobileRtComplaints,
        MobileWkPicking, MobileWkOperations, MobileWkLabel, MobileWkShipped
    };

    public static readonly IReadOnlyList<string> All = WebScreens.Concat(MobileScreens).ToArray();
}
