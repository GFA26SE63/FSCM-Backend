namespace FSCM.Application.Features.Reports;

/// <summary>
/// F76 sales-ranking query for a closed period. Dates are inclusive Vietnam-local calendar days;
/// revenue is recognized from delivered orders after promotions and F64 adjustments. The result is
/// scoped to the group/region the requesting user may view.
/// </summary>
public sealed record SalesRankingQuery(DateOnly FromDate, DateOnly ToDate, Guid? SalesGroupId);

public sealed record SalesRankingEntryResult(
    int Rank,
    Guid SalesUserId,
    string SalesUserName,
    Guid SalesGroupId,
    string SalesGroupCode,
    string SalesGroupName,
    Guid RegionId,
    int OrderCount,
    decimal RecognizedRevenue);

public sealed record SalesRankingResult(
    DateOnly FromDate,
    DateOnly ToDate,
    string Scope,
    IReadOnlyCollection<SalesRankingEntryResult> Entries);

public sealed record SalesKpiQuery(DateOnly FromDate, DateOnly ToDate, Guid? SalesGroupId, Guid? SalesUserId);

public sealed record SalesKpiMetricResult(
    string Code, string Name, decimal Target, decimal Actual, decimal CompletionRate, decimal Weight,
    decimal WeightedScore, string Unit);

public sealed record SalesKpiOrderResult(
    Guid OrderId, string OrderCode, Guid RetailerId, string RetailerCode, string RetailerName,
    DateOnly RecognitionDate, decimal RevenueBeforePromotion, decimal PromotionAmount,
    decimal RevenueAfterPromotion, decimal ApprovedAdjustmentAmount, decimal RecognizedRevenue);

public sealed record SalesKpiEntryResult(
    Guid SalesUserId, string SalesUserName, Guid SalesGroupId, string SalesGroupCode, string SalesGroupName,
    Guid RegionId, decimal OverallCompletionRate, decimal RecognizedRevenue,
    IReadOnlyCollection<SalesKpiMetricResult> Metrics,
    IReadOnlyCollection<SalesKpiOrderResult> Orders);

public sealed record SalesKpiResult(
    DateOnly FromDate, DateOnly ToDate, string Scope, int ConfigurationVersion,
    DateTimeOffset ConfigurationEffectiveFrom, string RecognitionRule,
    IReadOnlyCollection<SalesKpiEntryResult> Entries);
