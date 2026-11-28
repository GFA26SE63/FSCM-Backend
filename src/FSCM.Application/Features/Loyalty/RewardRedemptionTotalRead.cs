namespace FSCM.Application.Features.Loyalty;

/// <summary>Null quantity preserves unknown legacy redemption snapshots.</summary>
public sealed record RewardRedemptionTotalRead(Guid LoyaltyRewardId, decimal? Quantity);
