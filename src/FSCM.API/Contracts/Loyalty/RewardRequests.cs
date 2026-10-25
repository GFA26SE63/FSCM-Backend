namespace FSCM.API.Contracts.Loyalty;

public sealed record CreateRewardRequest(
    Guid LoyaltyProgramId, string Name, string? Description, string? ImageUrl,
    string RewardType, int RequiredPoints, decimal? DiscountAmount,
    decimal? DiscountPercent, int? AvailableQuantity);

public sealed record UpdateRewardRequest(
    string Name, string? Description, string? ImageUrl, string RewardType,
    int RequiredPoints, decimal? DiscountAmount, decimal? DiscountPercent,
    int? AvailableQuantity);
