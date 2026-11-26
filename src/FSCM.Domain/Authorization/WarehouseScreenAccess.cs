namespace FSCM.Domain.Authorization;

public static class WarehouseScreenAccess
{
    public static bool Allows(IReadOnlyCollection<string> grants, string kind, string reason, string status, bool readOnly)
    {
        if (grants.Contains(SystemScreenKeys.MobileWkOperations)) return true;
        if (reason == "Order")
            return grants.Contains(SystemScreenKeys.WarehouseOutbound) || grants.Contains(SystemScreenKeys.MobileWkPicking) ||
                (readOnly && status == "Posted" && grants.Contains(SystemScreenKeys.MobileWkShipped));
        if (reason == "Transfer") return grants.Contains(SystemScreenKeys.WarehouseTransfers);
        if (reason == "Factory") return grants.Contains(kind == "Request"
            ? SystemScreenKeys.WarehouseFactoryRequests : SystemScreenKeys.WarehouseInbound);
        return false;
    }
}
