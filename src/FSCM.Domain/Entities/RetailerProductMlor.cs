// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class RetailerProductMlor
{
    public Guid Id { get; set; }
    public Guid RetailerId { get; set; }
    public Guid ProductId { get; set; }
    public decimal MinimumLifeRatio { get; set; }
    public int? MinimumLifeHours { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
    public bool IsActive { get; set; }
}

