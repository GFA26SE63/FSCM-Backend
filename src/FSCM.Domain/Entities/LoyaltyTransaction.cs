// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class LoyaltyTransaction
{
    public Guid Id { get; set; }
    public Guid LoyaltyProgramId { get; set; }
    public Guid RetailerLoyaltyId { get; set; }
    public Guid? SalesOrderId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public int Points { get; set; }
    public int BalanceAfter { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
