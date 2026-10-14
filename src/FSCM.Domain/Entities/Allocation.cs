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
    public DateTimeOffset CreatedAt { get; set; }
}
