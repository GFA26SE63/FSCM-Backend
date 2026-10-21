namespace FSCM.Application.Features.Inventory;

public sealed record ReceiveGoodsLineData(
    Guid ProductId, string? SourceLotCode, decimal Quantity, DateOnly ExpirationDate);

public sealed record GoodsReceiptLineResult(
    Guid Id, Guid ProductId, string Sku, string ProductName, string? SourceLotCode,
    decimal ReceivedQuantity, DateOnly ExpirationDate, Guid BatchId, string BatchCode);

public sealed record GoodsReceiptResult(
    Guid Id, string ReceiptCode, Guid WarehouseId, string WarehouseCode, string WarehouseName,
    Guid CreatedByUserId, string Status, DateTimeOffset ReceivedAt, DateTimeOffset CreatedAt,
    IReadOnlyCollection<GoodsReceiptLineResult> Lines);

public sealed record BatchInventoryResult(
    Guid BatchId, string BatchCode, Guid WarehouseId, string WarehouseCode, string WarehouseName,
    Guid ProductId, string Sku, string ProductName, DateOnly ExpirationDate, string ExpiryStatus,
    string StockStatus, decimal OnHandQuantity, decimal ReservedQuantity,
    decimal AvailableQuantity, int Version, DateTimeOffset UpdatedAt);

public sealed record InventoryHistoryResult(
    Guid Id, Guid BatchId, string BatchCode, string WarehouseCode, string Sku,
    string TransactionType, string SourceType, Guid SourceId, string ReferenceCode, decimal OnHandDelta,
    decimal ReservedDelta, decimal OnHandAfter, decimal ReservedAfter,
    decimal AvailableAfter, string? Note, Guid CreatedByUserId, DateTimeOffset OccurredAt);

public sealed record ExpirySweepRunResult(
    Guid Id, DateOnly BusinessDate, string Status, int BatchesScanned,
    int StatusesUpdated, int BatchesExpired, string? Error,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt);

public sealed record InventoryExpiryHealthResult(
    string ScheduleTimeZone, string ScheduleLocalTime, bool SchedulerEnabled,
    ExpirySweepRunResult? LatestRun, int PendingOutboxMessages, int FailedOutboxMessages);
