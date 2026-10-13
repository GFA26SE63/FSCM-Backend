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
}
