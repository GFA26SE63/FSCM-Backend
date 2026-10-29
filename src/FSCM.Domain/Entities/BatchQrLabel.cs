// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class BatchQrLabel
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid CurrentWarehouseId { get; set; }

    /// <summary>The goods receipt this label traces back to (F40). Null for labels whose batch has no linked receipt line.</summary>
    public Guid? GoodsReceiptId { get; set; }

    public Guid? ParentLabelId { get; set; }
    public Guid? RootLabelId { get; set; }
    public string QrCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? BlobUrl { get; set; }
    public int Version { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? InvalidatedAt { get; set; }
}
