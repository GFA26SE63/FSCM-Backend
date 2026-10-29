namespace FSCM.API.Contracts.Loyalty;

public sealed record CreateRedemptionRequest(Guid RewardId, int Quantity, string? Note);
