// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class RewardRedemption
{
    public Guid Id { get; set; }
    public Guid RetailerId { get; set; }
    public Guid LoyaltyRewardId { get; set; }
    public Guid? LoyaltyTransactionId { get; set; }
    public Guid? SalesOrderId { get; set; }
    public string ClientRequestId { get; set; } = string.Empty;
    public int RedeemedPoints { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid RedeemedByUserId { get; set; }
    public DateTimeOffset RedeemedAt { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
