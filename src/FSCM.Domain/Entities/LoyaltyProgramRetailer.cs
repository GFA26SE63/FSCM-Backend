namespace FSCM.Domain.Entities;

public sealed class LoyaltyProgramRetailer
{
    public Guid Id { get; set; }
    public Guid LoyaltyProgramId { get; set; }
    public Guid RetailerId { get; set; }
}
