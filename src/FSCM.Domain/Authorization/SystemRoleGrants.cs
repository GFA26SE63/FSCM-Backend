namespace FSCM.Domain.Authorization;

/// <summary>One-time provisioning defaults. Existing role permissions are never replaced by these.</summary>
public static class SystemRoleGrants
{
    public static IReadOnlyList<string> For(string role)
    {
        IEnumerable<string> screens = role switch
        {
            SystemRoles.Admin => SystemScreenKeys.All,
            SystemRoles.Sales => SystemScreenKeys.MobileSalesScreens,
            SystemRoles.Retailer => SystemScreenKeys.MobileRetailerScreens,
            SystemRoles.Operator => new[] { "dashboard", "reports", "warehouse-report", "orders", "allocation", "debts", "refunds", "complaints", "batches", "readiness", "retailers", "loyalty", "redeem", "kpi", "fleet" },
            SystemRoles.WarehouseKeeper => new[] { "dashboard", "reports", "warehouse-inbound", "warehouse-factory-requests", "warehouse-outbound", "warehouse-transfers", "batches", "warehouse-trace", "disposal" }.Concat(SystemScreenKeys.MobileWarehouseScreens),
            SystemRoles.SalesManager => new[] { "dashboard", "reports", "regions", "kpi" },
            _ => []
        };
        return screens.Concat(ApiPermissionCatalog.All.Where(p => p.InitialRoles.Contains(role)).Select(p => p.Key)).ToArray();
    }
}
