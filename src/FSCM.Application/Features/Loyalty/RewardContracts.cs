using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Loyalty;

public sealed record CreateRewardData(
    Guid LoyaltyProgramId, string Name, string? Description, string? ImageUrl,
    string RewardType, int RequiredPoints, decimal? DiscountAmount,
    decimal? DiscountPercent, int? AvailableQuantity);

public sealed record UpdateRewardData(
    string Name, string? Description, string? ImageUrl, string RewardType,
    int RequiredPoints, decimal? DiscountAmount, decimal? DiscountPercent,
    int? AvailableQuantity);

public sealed record RewardResult(
    Guid Id, Guid LoyaltyProgramId, string Code, string Name, string? Description,
    string? ImageUrl, string RewardType, int RequiredPoints, decimal? DiscountAmount,
    decimal? DiscountPercent, int? AvailableQuantity, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record RewardView(LoyaltyReward Reward);
