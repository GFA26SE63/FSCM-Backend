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

    public static readonly IReadOnlyList<string> All = new[]
    {
        Dashboard, Reports, WarehouseReport, Kpi,
        Orders, Allocation, Debts, Refunds, Complaints,
        WarehouseInbound, WarehouseOutbound, WarehouseTransfers, Batches, WarehouseTrace, Readiness, BatchPromo, Disposal, Fleet,
        Retailers, Promos, Loyalty, Redeem, Gifts,
        Regions, Cats, CatMlor, Skus, Whs,
        Users, Roles, Config
    };
}
