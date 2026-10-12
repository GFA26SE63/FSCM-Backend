// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class OrderPromotion
{
    public Guid Id { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public Guid PromotionId { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public bool IsServerFinal { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
}
