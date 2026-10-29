// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class GoodsReceipt
{
    public Guid Id { get; set; }
    public string ReceiptCode { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public string? RequestHash { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ReceivedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
