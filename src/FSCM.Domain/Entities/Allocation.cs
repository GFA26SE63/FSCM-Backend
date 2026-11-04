// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class Allocation
{
    public Guid Id { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public Guid BatchId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public int AllocationSequence { get; set; }
    public decimal DistanceMeters { get; set; }
    public DateTimeOffset EstimatedArrivalAt { get; set; }
    public int RemainingLifeHoursAtArrival { get; set; }
    public decimal ShelfLifeScore { get; set; }
    public decimal DistanceScore { get; set; }
    public decimal CoverageScore { get; set; }
    public decimal WeightedScore { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal NetAmount { get; set; }
    public Guid? PromotionId { get; set; }
    public string? DiscountSourceType { get; set; }
    public string? DiscountSourceCode { get; set; }
    public string Status { get; set; } = string.Empty;

    // Price snapshot taken when the allocation was quoted (F98/F60). Nullable for rows created
    // before the snapshot existed. Locked at quote time; never recomputed from current tables.
    /// <summary>Per-carton list price minus the quantity promotion, before shelf-life discount.</summary>
    public decimal? PostPromotionUnitPrice { get; set; }
    public decimal? ShelfLifeDiscountPerUnit { get; set; }
    public decimal? RemainingShelfLifePercent { get; set; }
    public Guid? ShelfLifeDiscountTierId { get; set; }
    public string? ShelfLifeDiscountType { get; set; }
    public decimal? ShelfLifeDiscountValue { get; set; }
    public int? ShelfLifeDiscountTableVersion { get; set; }
    public string? PricingExplanation { get; set; }
    public DateTimeOffset? PricedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

