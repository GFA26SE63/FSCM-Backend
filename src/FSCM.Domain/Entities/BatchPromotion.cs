// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class BatchPromotion
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public Guid PromotionId { get; set; }
    public Guid AssignedByUserId { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}
