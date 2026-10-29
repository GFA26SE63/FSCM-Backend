// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// One row of the gather table: how much of one batch to take off the shelf in one trip, summed
// across every order that needs it. The split rows say who each unit belongs to.
public sealed class RunPickingGather
{
    public Guid Id { get; set; }
    public Guid RunPickingListId { get; set; }
    public Guid ProductId { get; set; }
    public string SkuSnapshot { get; set; } = string.Empty;
    public Guid BatchId { get; set; }
    public string BatchCodeSnapshot { get; set; } = string.Empty;
    public decimal RequiredQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
