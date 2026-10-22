// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// One band of remaining shelf life and the reduction it earns. MinRemainingPercent is inclusive and
// MaxRemainingPercent is exclusive, so adjacent bands meet without overlapping. The band carries no
// MLOR: how little life a customer accepts is a separate decision from how much the price drops.
public sealed class ShelfLifeDiscountTier
{
    public Guid Id { get; set; }
    public Guid ShelfLifeDiscountTableId { get; set; }
    public string Scope { get; set; } = string.Empty;
    public Guid? ProductId { get; set; }
    public Guid? ProductCategoryId { get; set; }
    public decimal MinRemainingPercent { get; set; }
    public decimal MaxRemainingPercent { get; set; }
    public string DiscountType { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
