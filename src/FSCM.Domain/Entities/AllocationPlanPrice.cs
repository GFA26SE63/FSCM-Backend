// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// The price of one allocated part, frozen against one plan version. Every component is stored, not
// just the total, so picking, delivery, collection and complaints read the same number instead of
// recomputing one that could drift.
public sealed class AllocationPlanPrice
{
    public Guid Id { get; set; }
    public Guid AllocationPlanVersionId { get; set; }
    public Guid AllocationRunLineId { get; set; }
    public Guid BatchId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Price after ordinary promotion, before any near-expiry reduction.</summary>
    public decimal BaseNetUnitPrice { get; set; }

    public decimal RemainingShelfLifePercent { get; set; }
    public Guid? ShelfLifeDiscountTierId { get; set; }
    public string? DiscountType { get; set; }
    public decimal? DiscountValue { get; set; }
    public decimal ShelfLifeDiscountPerUnit { get; set; }
    public decimal FinalNetUnitPrice { get; set; }
    public decimal Amount { get; set; }
    public int DiscountTableVersion { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
