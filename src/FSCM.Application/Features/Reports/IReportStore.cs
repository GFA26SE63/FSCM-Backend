namespace FSCM.Application.Features.Reports;

/// <summary>One Sales rep's aggregated recognized revenue for a period, before ranking.</summary>
public sealed record SalesRevenueRow(
    Guid SalesUserId,
    string SalesUserName,
    Guid SalesGroupId,
    string SalesGroupCode,
    string SalesGroupName,
    Guid RegionId,
    int OrderCount,
    decimal RecognizedRevenue);

public sealed record SalesKpiOrderRow(
    Guid OrderId, string OrderCode, Guid RetailerId, string RetailerCode, string RetailerName,
    Guid SalesUserId, string SalesUserName, Guid SalesGroupId, string SalesGroupCode, string SalesGroupName,
    Guid RegionId, DateOnly RecognitionDate, DateOnly RetailerCreatedDate, decimal RevenueBeforePromotion, decimal PromotionAmount,
    decimal RevenueAfterPromotion, decimal ApprovedAdjustmentAmount, decimal RecognizedRevenue);

public sealed record SalesKpiConfigurationRow(string Json, int Version, DateTimeOffset EffectiveFrom);

public interface IReportStore
{
    /// <summary>Active SalesGroup ids the user is a member of (used for SalesManager scoping).</summary>
    Task<IReadOnlyCollection<Guid>> ListActiveGroupIdsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Whether the user is an active member of the given group.</summary>
    Task<bool> IsActiveGroupMemberAsync(Guid userId, Guid salesGroupId, CancellationToken cancellationToken);

    /// <summary>
    /// Aggregates recognized revenue per Sales rep over delivered orders whose delivered date falls
    /// in the inclusive [fromDate, toDate] window, restricted to <paramref name="salesGroupIds"/>
    /// (null = no group restriction). Revenue is the F64-adjusted payment amount when a ledger
    /// exists, else the post-promotion order final amount. Shelf-life discounts are not recomputed.
    /// </summary>
    Task<IReadOnlyCollection<SalesRevenueRow>> AggregateSalesRevenueAsync(
        DateOnly fromDate,
        DateOnly toDate,
        IReadOnlyCollection<Guid>? salesGroupIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SalesKpiOrderRow>> ListRecognizedOrdersAsync(
        DateOnly fromDate, DateOnly toDate, IReadOnlyCollection<Guid>? salesGroupIds,
        Guid? salesUserId, CancellationToken cancellationToken);

    Task<SalesKpiConfigurationRow?> FindSalesKpiConfigurationAsync(
        DateOnly periodEnd, CancellationToken cancellationToken);
}
