// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class SalesOrderLine
{
    public Guid Id { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid ProductId { get; set; }
    public int LineNumber { get; set; }
    public string SkuSnapshot { get; set; } = string.Empty;
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid ProductPriceId { get; set; }
    public DateTimeOffset ProductPriceEffectiveFrom { get; set; }
    public DateTimeOffset? ProductPriceEffectiveTo { get; set; }
    public DateTimeOffset PricedAt { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DiscountPercent { get; set; }
    public string? DiscountSourceType { get; set; }
    public string? DiscountSourceCode { get; set; }
    public decimal NetAmount { get; set; }
    public bool FinalPricePendingAllocation { get; set; }
    public string MlorSourceScope { get; set; } = string.Empty;
    public Guid? MlorSourceRuleId { get; set; }
    public int MlorSourceVersion { get; set; }
    public Guid? MlorProductCategoryId { get; set; }
    public decimal MlorMinimumLifeRatio { get; set; }
    public int? MlorMinimumLifeHours { get; set; }
    public DateTimeOffset MlorResolvedAt { get; set; }
    public DateTimeOffset? EstimatedArrivalAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
