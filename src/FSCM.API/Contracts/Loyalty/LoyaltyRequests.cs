namespace FSCM.API.Contracts.Loyalty;

public sealed record CreateLoyaltyProgramRequest(
    string Name, string? Description, decimal SpendAmountPerPoint,
    int MinimumRedeemPoints, DateTimeOffset StartAt, DateTimeOffset EndAt,
    DateTimeOffset? PointsExpireAt, IReadOnlyCollection<Guid> RetailerIds);

public sealed record UpdateLoyaltyProgramRequest(
    string Name, string? Description, decimal SpendAmountPerPoint,
    int MinimumRedeemPoints, DateTimeOffset StartAt, DateTimeOffset EndAt,
    DateTimeOffset? PointsExpireAt, IReadOnlyCollection<Guid> RetailerIds);
