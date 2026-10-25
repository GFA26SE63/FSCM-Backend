using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Loyalty;

public sealed record CreateLoyaltyProgramData(
    string Name, string? Description, decimal SpendAmountPerPoint,
    int MinimumRedeemPoints, DateTimeOffset StartAt, DateTimeOffset EndAt,
    DateTimeOffset? PointsExpireAt, IReadOnlyCollection<Guid> RetailerIds);

public sealed record UpdateLoyaltyProgramData(
    string Name, string? Description, decimal SpendAmountPerPoint,
    int MinimumRedeemPoints, DateTimeOffset StartAt, DateTimeOffset EndAt,
    DateTimeOffset? PointsExpireAt, IReadOnlyCollection<Guid> RetailerIds);

public sealed record LoyaltyProgramResult(
    Guid Id, string Code, string Name, string? Description,
    decimal SpendAmountPerPoint, int MinimumRedeemPoints,
    DateTimeOffset StartAt, DateTimeOffset EndAt, DateTimeOffset? PointsExpireAt,
    IReadOnlyCollection<Guid> RetailerIds, bool IsActive,
    Guid CreatedByUserId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record LoyaltyProgramView(LoyaltyProgram Program);
