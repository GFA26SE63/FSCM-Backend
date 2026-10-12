// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class RetailerLoyalty
{
    public Guid Id { get; set; }
    public Guid RetailerId { get; set; }
    public int CurrentPoints { get; set; }
    public int LifetimeEarnedPoints { get; set; }
    public int LifetimeRedeemedPoints { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

