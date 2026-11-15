namespace FSCM.Domain.Entities;

public sealed class WarehouseDocument
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Kind { get; set; } = ""; // Request, Issue, Receipt
    public string Reason { get; set; } = ""; // Transfer, Order, Factory
    public Guid WarehouseId { get; set; }
    public Guid? DestinationWarehouseId { get; set; }
    public Guid? SourceDocumentId { get; set; }
    public Guid? GoodsReceiptId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Status { get; set; } = "Open";
    public string OperationKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string? Note { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
}

public sealed class WarehouseDocumentLine
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? BatchId { get; set; }
    public Guid? PickingItemId { get; set; }
    public Guid? PickingListId { get; set; }
    public Guid? SalesOrderId { get; set; }
    public string? OrderCode { get; set; }
    public string Sku { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string? BatchCode { get; set; }
    public DateOnly ProductionDate { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public decimal Quantity { get; set; }
    public string? SourceLotCode { get; set; }
}

public sealed class WarehouseDocumentCarton
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid LineId { get; set; }
    public string CartonId { get; set; } = "";
    public Guid? LabelId { get; set; }
    public Guid? ScannedByUserId { get; set; }
    public DateTimeOffset? ScannedAt { get; set; }
}
