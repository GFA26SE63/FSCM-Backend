// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class PickingItem
{
    public Guid Id { get; set; }
    public Guid PickingListId { get; set; }
    public Guid AllocationId { get; set; }
    public Guid SourceBatchId { get; set; }
    public Guid? StockOutBatchId { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ScannedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
