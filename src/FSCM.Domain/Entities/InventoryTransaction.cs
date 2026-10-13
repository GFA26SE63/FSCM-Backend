namespace FSCM.Domain.Entities;

public sealed class InventoryTransaction
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? GoodsReceiptId { get; set; }
    public Guid? GoodsReceiptLineId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string ReferenceCode { get; set; } = string.Empty;
    public decimal PhysicalQuantityDelta { get; set; }
    public decimal HeldQuantityDelta { get; set; }
    public decimal ReservedQuantityDelta { get; set; }
    public decimal PhysicalQuantityAfter { get; set; }
    public decimal HeldQuantityAfter { get; set; }
    public decimal ReservedQuantityAfter { get; set; }
    public decimal AvailableQuantityAfter { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
