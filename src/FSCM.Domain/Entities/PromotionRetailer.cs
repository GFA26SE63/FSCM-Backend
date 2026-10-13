// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class PromotionRetailer
{
    public Guid Id { get; set; }
    public Guid PromotionId { get; set; }
    public Guid RetailerId { get; set; }
}
