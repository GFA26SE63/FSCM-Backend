using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Loyalty;

public sealed record LoyaltyLedgerEntry(
    Guid TransactionId, string TransactionType, int Points, int BalanceAfter,
    string? Note, DateTimeOffset CreatedAt, Guid? SalesOrderId, string? OrderCode,
    Guid? RedemptionId, string? RedemptionStatus);

public sealed record LoyaltyCardData(
    Retailer Retailer, RetailerLoyalty? Balance, LoyaltyProgram? Program,
    IReadOnlyCollection<LoyaltyLedgerEntry> Ledger);

public sealed record LoyaltyCardResult(
    Guid RetailerId, string RetailerCode, string RetailerName,
    Guid? ProgramId, string? ProgramCode, string? ProgramName,
    int AvailablePoints, int HeldPoints, int PendingPoints, int ExpiredPoints,
    DateTimeOffset? PointsExpireAt, IReadOnlyCollection<LoyaltyLedgerEntry> Ledger);
