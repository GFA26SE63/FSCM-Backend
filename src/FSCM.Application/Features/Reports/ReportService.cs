using FSCM.Domain.Authorization;
using System.Text.Json;

namespace FSCM.Application.Features.Reports;

/// <summary>
/// F76: builds the Sales ranking for a closed period from recognized revenue after promotions and
/// F64 adjustments. Uses the same delivered-order + payment-ledger source the KPI reads (F75) so the
/// numbers do not diverge. Results are scoped: Admin/Operator see all reps, a SalesManager sees reps
/// in their own active group(s), and a Sales rep sees only their own group(s).
/// </summary>
public sealed class ReportService(IReportStore store, TimeProvider timeProvider)
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<SalesRankingResult> GetSalesRankingAsync(
        SalesRankingQuery query, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        if (actorRole is not (SystemRoles.Admin or SystemRoles.Operator or
            SystemRoles.SalesManager or SystemRoles.Sales))
            throw ReportOperationException.Forbidden("Role cannot view the sales ranking.");
        if (query.ToDate < query.FromDate)
            throw ReportOperationException.Validation("The end date cannot be before the start date.");
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(VietnamOffset).DateTime);
        if (query.FromDate > today)
            throw ReportOperationException.Validation("The period cannot start in the future.");

        var (groupIds, scope) = await ResolveScopeAsync(query.SalesGroupId, actorUserId, actorRole, cancellationToken);
        var rows = await store.AggregateSalesRevenueAsync(query.FromDate, query.ToDate, groupIds, cancellationToken);

        var ranked = rows
            .OrderByDescending(row => row.RecognizedRevenue)
            .ThenBy(row => row.SalesUserName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.SalesUserId)
            .Select((row, index) => new SalesRankingEntryResult(
                index + 1, row.SalesUserId, row.SalesUserName, row.SalesGroupId,
                row.SalesGroupCode, row.SalesGroupName, row.RegionId,
                row.OrderCount, row.RecognizedRevenue))
            .ToArray();

        return new SalesRankingResult(query.FromDate, query.ToDate, scope, ranked);
    }

    public async Task<SalesKpiResult> GetSalesKpiAsync(
        SalesKpiQuery query, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        ValidatePeriod(query.FromDate, query.ToDate);
        var (groupIds, scope) = await ResolveScopeAsync(query.SalesGroupId, actorUserId, actorRole, cancellationToken);
        var requestedSalesUserId = query.SalesUserId;
        if (actorRole == SystemRoles.Sales)
        {
            if (requestedSalesUserId.HasValue && requestedSalesUserId != actorUserId)
                throw ReportOperationException.Forbidden("Sales users can only view their own KPI.");
            requestedSalesUserId = actorUserId;
        }

        var configurationRow = await store.FindSalesKpiConfigurationAsync(query.ToDate, cancellationToken)
            ?? throw ReportOperationException.Validation("Sales KPI configuration is missing.");
        var configuration = ParseConfiguration(configurationRow.Json);
        var rows = await store.ListRecognizedOrdersAsync(
            query.FromDate, query.ToDate, groupIds, requestedSalesUserId, cancellationToken);

        var entries = rows.GroupBy(row => new
            {
                row.SalesUserId, row.SalesUserName, row.SalesGroupId, row.SalesGroupCode,
                row.SalesGroupName, row.RegionId
            })
            .Select(group => BuildKpiEntry(group.Key.SalesUserId, group.Key.SalesUserName,
                group.Key.SalesGroupId, group.Key.SalesGroupCode, group.Key.SalesGroupName,
                group.Key.RegionId, query.FromDate, group.ToArray(), configuration))
            .OrderByDescending(entry => entry.OverallCompletionRate)
            .ThenBy(entry => entry.SalesUserName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new(query.FromDate, query.ToDate, scope, configurationRow.Version,
            configurationRow.EffectiveFrom,
            "Delivered order; Payment.DeliveredDate, otherwise ServerReceivedAt in Asia/Ho_Chi_Minh; Payment.AdjustedAmount, otherwise Order.FinalAmount.",
            entries);
    }

    public async Task<ProductRankingResult> GetProductRankingAsync(
        ProductRankingQuery query, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        ValidatePeriod(query.FromDate, query.ToDate);
        if (query.Criterion is not (ProductRankingCriteria.Quantity or ProductRankingCriteria.Revenue))
            throw ReportOperationException.Validation("Criterion must be Quantity or Revenue.");

        var (groupIds, scope) = await ResolveScopeAsync(
            query.SalesGroupId, actorUserId, actorRole, cancellationToken);
        var rows = await store.AggregateProductSalesAsync(
            query.FromDate, query.ToDate, groupIds, cancellationToken);
        var ordered = query.Criterion == ProductRankingCriteria.Quantity
            ? rows.OrderByDescending(row => row.NormalizedSoldQuantity)
                .ThenByDescending(row => row.AdjustedNetRevenue)
            : rows.OrderByDescending(row => row.AdjustedNetRevenue)
                .ThenByDescending(row => row.NormalizedSoldQuantity);
        var entries = ordered.ThenBy(row => row.Sku, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ProductId)
            .Select((row, index) => new ProductRankingEntryResult(
                index + 1, row.ProductId, row.Sku, row.ProductName,
                row.NormalizedSoldQuantity, "carton", row.AdjustedNetRevenue))
            .ToArray();

        return new(query.FromDate, query.ToDate, query.Criterion, scope, "carton",
            "Delivered orders recognized on Payment.DeliveredDate, otherwise ServerReceivedAt in Asia/Ho_Chi_Minh; approved allocation fragment net amounts, with approved order adjustments allocated proportionally by product.",
            entries);
    }

    private async Task<(IReadOnlyCollection<Guid>? GroupIds, string Scope)> ResolveScopeAsync(
        Guid? requestedGroupId, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
    {
        if (actorRole is SystemRoles.Admin or SystemRoles.Operator)
        {
            if (requestedGroupId.HasValue) return ([requestedGroupId.Value], "Group");
            return (null, "All");
        }

        // SalesManager and Sales are both bounded to their own active group memberships.
        var memberGroups = await store.ListActiveGroupIdsAsync(actorUserId, cancellationToken);
        if (memberGroups.Count == 0)
            throw ReportOperationException.Forbidden("You are not an active member of any sales group.");
        if (requestedGroupId.HasValue)
        {
            if (!memberGroups.Contains(requestedGroupId.Value))
                throw ReportOperationException.Forbidden("You cannot view the ranking for this sales group.");
            return ([requestedGroupId.Value], "Group");
        }
        return (memberGroups, actorRole == SystemRoles.SalesManager ? "Group" : "OwnGroup");
    }

    private void ValidatePeriod(DateOnly fromDate, DateOnly toDate)
    {
        if (toDate < fromDate)
            throw ReportOperationException.Validation("The end date cannot be before the start date.");
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(VietnamOffset).DateTime);
        if (fromDate > today)
            throw ReportOperationException.Validation("The period cannot start in the future.");
    }

    private static SalesKpiEntryResult BuildKpiEntry(
        Guid salesUserId, string salesUserName, Guid salesGroupId, string salesGroupCode,
        string salesGroupName, Guid regionId, DateOnly periodFrom, IReadOnlyCollection<SalesKpiOrderRow> rows,
        SalesKpiConfiguration configuration)
    {
        var successfulOrders = rows.Count;
        var retailersServed = rows.Select(row => row.RetailerId).Distinct().Count();
        var newRetailers = rows.Where(row => row.RetailerCreatedDate >= periodFrom)
            .Select(row => row.RetailerId).Distinct().Count();
        var revenue = rows.Sum(row => row.RecognizedRevenue);
        var promotion = rows.Sum(row => row.PromotionAmount);
        var promotionRatio = revenue <= 0 ? 0 : promotion / revenue;

        var metrics = new[]
        {
            Metric("successfulOrders", "Đơn thành công", configuration.SuccessfulOrdersTarget,
                successfulOrders, configuration.SuccessfulOrdersWeight, "order"),
            Metric("retailersServed", "Điểm bán đã phục vụ", configuration.RetailersServedTarget,
                retailersServed, configuration.RetailersServedWeight, "retailer"),
            Metric("newRetailers", "Điểm bán mới", configuration.NewRetailersTarget,
                newRetailers, configuration.NewRetailersWeight, "retailer"),
            InverseMetric("promotionRevenueRatio", "Tỷ lệ khuyến mãi trên doanh thu",
                configuration.PromotionRevenueRatioMaximum, promotionRatio,
                configuration.PromotionRevenueRatioWeight, "ratio")
        };
        var orders = rows.OrderByDescending(row => row.RecognitionDate).ThenBy(row => row.OrderCode)
            .Select(row => new SalesKpiOrderResult(row.OrderId, row.OrderCode, row.RetailerId,
                row.RetailerCode, row.RetailerName, row.RecognitionDate, row.RevenueBeforePromotion,
                row.PromotionAmount, row.RevenueAfterPromotion, row.ApprovedAdjustmentAmount,
                row.RecognizedRevenue)).ToArray();
        return new(salesUserId, salesUserName, salesGroupId, salesGroupCode, salesGroupName, regionId,
            decimal.Round(metrics.Sum(metric => metric.WeightedScore), 4), revenue, metrics, orders);
    }

    private static SalesKpiMetricResult Metric(
        string code, string name, decimal target, decimal actual, decimal weight, string unit)
    {
        var completion = target == 0 ? 0 : decimal.Round(actual / target, 4);
        return new(code, name, target, actual, completion, weight,
            decimal.Round(Math.Min(1m, completion) * weight, 4), unit);
    }

    private static SalesKpiMetricResult InverseMetric(
        string code, string name, decimal target, decimal actual, decimal weight, string unit)
    {
        var completion = actual <= target || actual == 0 ? 1m : decimal.Round(target / actual, 4);
        return new(code, name, target, actual, completion, weight,
            decimal.Round(Math.Min(1m, completion) * weight, 4), unit);
    }

    private static SalesKpiConfiguration ParseConfiguration(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<SalesKpiConfiguration>(json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return value is null ? throw new JsonException() : value;
        }
        catch (JsonException)
        {
            throw ReportOperationException.Validation("Sales KPI configuration is invalid.");
        }
    }

    private sealed record SalesKpiConfiguration(
        decimal SuccessfulOrdersTarget, decimal RetailersServedTarget, decimal NewRetailersTarget,
        decimal PromotionRevenueRatioMaximum, decimal SuccessfulOrdersWeight,
        decimal RetailersServedWeight, decimal NewRetailersWeight, decimal PromotionRevenueRatioWeight);
}
