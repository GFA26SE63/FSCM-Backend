using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Loyalty;

public sealed record CreateRedemptionData(Guid RewardId, int Quantity, string ClientRequestId, string? Note);

public sealed record RedemptionResult(
    Guid Id, Guid RetailerId, Guid RewardId, string RewardCode, string RewardName,
    int Quantity, int RedeemedPoints, string Status, int BalanceAfter,
    string ClientRequestId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    string? RetailerCode = null, string? RetailerName = null);

public sealed record RedemptionView(RewardRedemption Redemption, LoyaltyReward Reward, int BalanceAfter,
    string? RetailerCode = null, string? RetailerName = null);
