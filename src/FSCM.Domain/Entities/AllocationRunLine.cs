// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

// One order line as the run froze it. The effective MLOR and base price are copied here rather than
// read live, so a configuration change mid-run cannot alter a plan that is already being scored.
public sealed class AllocationRunLine
{
    public Guid Id { get; set; }
    public Guid AllocationRunOrderId { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public Guid ProductId { get; set; }
    public string SkuSnapshot { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public decimal OrderedQuantity { get; set; }
    public int TotalShelfLifeHours { get; set; }
    public decimal EffectiveMlorRatio { get; set; }
    public string MlorSource { get; set; } = string.Empty;
    public decimal ListUnitPrice { get; set; }
    public decimal BaseNetUnitPrice { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
