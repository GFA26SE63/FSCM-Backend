// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class PromotionProduct
{
    public Guid Id { get; set; }
    public Guid PromotionId { get; set; }
    public Guid ProductId { get; set; }
}

