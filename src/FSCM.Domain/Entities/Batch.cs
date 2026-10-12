// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class Batch
{
    public Guid Id { get; set; }
    public Guid? ParentBatchId { get; set; }
    public Guid? RootBatchId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? GoodsReceiptLineId { get; set; }
    public string BatchCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public DateTimeOffset ExpirationAt { get; set; }
    public string ExpiryStatus { get; set; } = string.Empty;
    public string StockStatus { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

