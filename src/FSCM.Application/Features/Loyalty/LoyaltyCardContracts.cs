using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Loyalty;

public sealed record LoyaltyLedgerEntry(
    Guid TransactionId, string TransactionType, int Points, int BalanceAfter,
    string? Note, DateTimeOffset CreatedAt, Guid? SalesOrderId, string? OrderCode,
    Guid? RedemptionId, string? RedemptionStatus);

public sealed record LoyaltyCardData(
    Retailer Retailer, RetailerLoyalty? Balance, LoyaltyProgram? Program,
    IReadOnlyCollection<LoyaltyLedgerEntry> Ledger)
{
    public IReadOnlyCollection<LoyaltyProgram> EligiblePrograms { get; init; } = [];
}

public sealed record LoyaltyCardProgram(Guid Id, string Code, string Name, decimal SpendAmountPerPoint,
    bool IsActive, DateTimeOffset StartAt, DateTimeOffset EndAt, DateTimeOffset? PointsExpireAt);

public sealed record LoyaltyCardResult(
    Guid RetailerId, string RetailerCode, string RetailerName,
    Guid? ProgramId, string? ProgramCode, string? ProgramName,
    int AvailablePoints, int HeldPoints, int PendingPoints, int ExpiredPoints,
    DateTimeOffset? PointsExpireAt, IReadOnlyCollection<LoyaltyLedgerEntry> Ledger)
{
    public IReadOnlyCollection<LoyaltyCardProgram> EligiblePrograms { get; init; } = [];
}
