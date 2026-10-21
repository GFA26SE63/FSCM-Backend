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
    public const string OrderReserve = "OrderReserve";
    public const string OrderRelease = "OrderRelease";
    public const string OrderIssue = "OrderIssue";
    public const string TransferDispatch = "TransferDispatch";
    public const string TransferReceipt = "TransferReceipt";
}

public static class InventoryTransactionSourceTypes
{
    public const string GoodsReceipt = "GoodsReceipt";
    public const string Allocation = "Allocation";
    public const string InternalTransfer = "InternalTransfer";
}

/// <summary>Domain integration events emitted to the transactional outbox.</summary>
public static class InventoryOutboxEventTypes
{
    /// <summary>Raised by the nightly date tracker when a batch crosses its expiration date.</summary>
    public const string BatchExpired = "BatchExpired";
}

public static class OutboxMessageStatuses
{
    public const string Pending = "Pending";
    public const string Processed = "Processed";
    public const string Failed = "Failed";
}

public static class OutboxAggregateTypes
{
    public const string Batch = "Batch";
}

public static class ExpirySweepStatuses
{
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

public static class InventoryNotificationTypes
{
    public const string BatchExpired = "BatchExpired";
}
