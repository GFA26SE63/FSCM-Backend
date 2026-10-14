namespace FSCM.Application.Features.Inventory;

public static class GoodsReceiptStatuses
{
    public const string Received = "Received";
}

public static class BatchExpiryStatuses
{
    public const string Valid = "Valid";
    public const string NearExpiry = "NearExpiry";
    public const string Expired = "Expired";
}

public static class BatchStockStatuses
{
    public const string InStock = "InStock";
    public const string Depleted = "Depleted";
}

public static class InventoryTransactionTypes
{
    public const string GoodsReceipt = "GoodsReceipt";
    public const string OrderHold = "OrderHold";
    public const string OrderReserve = "OrderReserve";
    public const string OrderRelease = "OrderRelease";
    public const string OrderIssue = "OrderIssue";
    public const string TransferDispatch = "TransferDispatch";
    public const string TransferReceipt = "TransferReceipt";
}
