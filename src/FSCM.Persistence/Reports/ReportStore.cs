using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Reports;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Persistence.DbContexts;
using FSCM.Application.Features.WarehouseConfiguration;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Reports;

public sealed class ReportStore(AppDbContext dbContext) : IReportStore
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<IReadOnlyCollection<Guid>> ListActiveGroupIdsAsync(
        Guid userId, CancellationToken cancellationToken) =>
        await dbContext.SalesGroupMemberRecords.AsNoTracking()
            .Where(item => item.UserAccountId == userId && item.IsActive)
            .Select(item => item.SalesGroupId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

    public Task<bool> IsActiveGroupMemberAsync(Guid userId, Guid salesGroupId, CancellationToken cancellationToken) =>
        dbContext.SalesGroupMemberRecords.AsNoTracking().AnyAsync(item =>
            item.UserAccountId == userId && item.SalesGroupId == salesGroupId && item.IsActive, cancellationToken);

    /// <summary>
    /// Review 2 / D11: revenue belongs to the period in which the order was Completed (delivery,
    /// payment and claims settled), so a closed period never changes after the fact. Orders with no
    /// recorded completion fall back to the delivered/received date.
    /// </summary>
    private async Task<Dictionary<Guid, DateOnly>> LoadCompletionDatesAsync(
        IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken)
    {
        var rows = await dbContext.OrderStatusHistoryRecords.AsNoTracking()
            .Where(item => item.ToStatus == OrderStatuses.Completed && orderIds.Contains(item.SalesOrderId))
            .Select(item => new { item.SalesOrderId, item.ChangedAt })
            .ToArrayAsync(cancellationToken);
        return rows.GroupBy(item => item.SalesOrderId).ToDictionary(
            group => group.Key,
            group => DateOnly.FromDateTime(group.Max(item => item.ChangedAt).ToOffset(VietnamOffset).DateTime));
    }

    public async Task<IReadOnlyCollection<SalesRevenueRow>> AggregateSalesRevenueAsync(
        DateOnly fromDate,
        DateOnly toDate,
        IReadOnlyCollection<Guid>? salesGroupIds,
        CancellationToken cancellationToken)
    {
        // Completed orders are the recognized-revenue source (same as the KPI, F75). Review 2:
        // revenue is recognized only when the order is fully closed (delivery + payment + claims
        // settled), not merely delivered. The recognized amount is the F64-adjusted payment amount
        // when a ledger exists, else the post-promotion order total. The recognition date is the
        // payment delivered date, else the order's Vietnam-local received date — both bounded to the
        // inclusive [fromDate, toDate] window.
        var groupSet = salesGroupIds?.ToHashSet();

        var rows = await (
            from order in dbContext.SalesOrderRecords.AsNoTracking()
            where order.Status == OrderStatuses.Completed
            join payment in dbContext.PaymentRecords.AsNoTracking()
                on order.Id equals payment.SalesOrderId into payments
            from payment in payments.DefaultIfEmpty()
            join user in dbContext.UserAccountRecords.AsNoTracking()
                on order.CreatedByUserId equals user.Id
            join salesGroup in dbContext.SalesGroupRecords.AsNoTracking()
                on order.SalesGroupId equals salesGroup.Id
            select new
            {
                OrderId = order.Id,
                order.CreatedByUserId,
                UserName = user.FullName,
                order.SalesGroupId,
                GroupCode = salesGroup.Code,
                GroupName = salesGroup.Name,
                salesGroup.RegionId,
                Revenue = payment != null ? payment.AdjustedAmount : order.FinalAmount,
                DeliveredDate = payment != null && payment.DeliveredDate != null
                    ? payment.DeliveredDate
                    : (DateOnly?)null,
                order.ServerReceivedAt
            }).ToArrayAsync(cancellationToken);

        var completedOn = await LoadCompletionDatesAsync(rows.Select(row => row.OrderId).ToArray(), cancellationToken);
        var filtered = rows
            .Where(row => groupSet == null || groupSet.Contains(row.SalesGroupId))
            .Where(row =>
            {
                var recognitionDate = completedOn.TryGetValue(row.OrderId, out var completed) ? completed
                    : row.DeliveredDate
                    ?? DateOnly.FromDateTime(row.ServerReceivedAt.ToOffset(VietnamOffset).DateTime);
                return recognitionDate >= fromDate && recognitionDate <= toDate;
            });

        return filtered
            .GroupBy(row => new
            {
                row.CreatedByUserId, row.UserName, row.SalesGroupId,
                row.GroupCode, row.GroupName, row.RegionId
            })
            .Select(group => new SalesRevenueRow(
                group.Key.CreatedByUserId,
                group.Key.UserName,
                group.Key.SalesGroupId,
                group.Key.GroupCode,
                group.Key.GroupName,
                group.Key.RegionId,
                group.Count(),
                group.Sum(row => row.Revenue)))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<SalesKpiOrderRow>> ListRecognizedOrdersAsync(
        DateOnly fromDate, DateOnly toDate, IReadOnlyCollection<Guid>? salesGroupIds,
        Guid? salesUserId, CancellationToken cancellationToken)
    {
        var groupSet = salesGroupIds?.ToHashSet();
        var rows = await (
            from order in dbContext.SalesOrderRecords.AsNoTracking()
            where order.Status == OrderStatuses.Completed
            join payment in dbContext.PaymentRecords.AsNoTracking()
                on order.Id equals payment.SalesOrderId into payments
            from payment in payments.DefaultIfEmpty()
            join user in dbContext.UserAccountRecords.AsNoTracking()
                on order.CreatedByUserId equals user.Id
            join salesGroup in dbContext.SalesGroupRecords.AsNoTracking()
                on order.SalesGroupId equals salesGroup.Id
            join retailer in dbContext.RetailerRecords.AsNoTracking()
                on order.RetailerId equals retailer.Id
            select new
            {
                Order = order, Payment = payment, UserName = user.FullName,
                GroupCode = salesGroup.Code, GroupName = salesGroup.Name, salesGroup.RegionId,
                RetailerCreatedAt = retailer.CreatedAt
            }).ToArrayAsync(cancellationToken);

        var kpiCompletedOn = await LoadCompletionDatesAsync(rows.Select(row => row.Order.Id).ToArray(), cancellationToken);
        return rows.Where(row => groupSet == null || groupSet.Contains(row.Order.SalesGroupId))
            .Where(row => !salesUserId.HasValue || row.Order.CreatedByUserId == salesUserId.Value)
            .Select(row => new
            {
                Row = row,
                RecognitionDate = kpiCompletedOn.TryGetValue(row.Order.Id, out var completed) ? completed
                    : row.Payment?.DeliveredDate
                    ?? DateOnly.FromDateTime(row.Order.ServerReceivedAt.ToOffset(VietnamOffset).DateTime)
            })
            .Where(row => row.RecognitionDate >= fromDate && row.RecognitionDate <= toDate)
            .Select(item => new SalesKpiOrderRow(
                item.Row.Order.Id, item.Row.Order.OrderCode, item.Row.Order.RetailerId,
                item.Row.Order.RetailerCodeSnapshot, item.Row.Order.RetailerNameSnapshot,
                item.Row.Order.CreatedByUserId, item.Row.UserName, item.Row.Order.SalesGroupId,
                item.Row.GroupCode, item.Row.GroupName, item.Row.RegionId, item.RecognitionDate,
                DateOnly.FromDateTime(item.Row.RetailerCreatedAt.ToOffset(VietnamOffset).DateTime),
                item.Row.Order.SubtotalAmount, item.Row.Order.DiscountAmount,
                item.Row.Order.FinalAmount,
                Math.Max(0, item.Row.Order.FinalAmount - (item.Row.Payment?.AdjustedAmount ?? item.Row.Order.FinalAmount)),
                item.Row.Payment?.AdjustedAmount ?? item.Row.Order.FinalAmount))
            .ToArray();
    }

    public async Task<SalesKpiConfigurationRow?> FindSalesKpiConfigurationAsync(
        DateOnly periodEnd, CancellationToken cancellationToken)
    {
        var exclusiveEndUtc = new DateTimeOffset(periodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), VietnamOffset)
            .ToUniversalTime();
        return
        await dbContext.SystemConfigurationRecords.AsNoTracking()
            .Where(item => item.ConfigKey == BusinessConfigurationKeys.SalesKpi && item.EffectiveFrom < exclusiveEndUtc)
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenByDescending(item => item.Version)
            .Select(item => new SalesKpiConfigurationRow(item.ConfigValue, item.Version, item.EffectiveFrom))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<ProductSalesRow>> AggregateProductSalesAsync(
        DateOnly fromDate, DateOnly toDate, IReadOnlyCollection<Guid>? salesGroupIds,
        CancellationToken cancellationToken)
    {
        var groupSet = salesGroupIds?.ToHashSet();
        var orderLines = await (
            from order in dbContext.SalesOrderRecords.AsNoTracking()
            where order.Status == OrderStatuses.Completed
            join payment in dbContext.PaymentRecords.AsNoTracking()
                on order.Id equals payment.SalesOrderId into payments
            from payment in payments.DefaultIfEmpty()
            join line in dbContext.SalesOrderLineRecords.AsNoTracking()
                on order.Id equals line.SalesOrderId
            join product in dbContext.ProductRecords.AsNoTracking()
                on line.ProductId equals product.Id
            select new
            {
                Order = order, Payment = payment, Line = line,
                ProductSku = product.Sku, ProductName = product.Name
            }).ToArrayAsync(cancellationToken);

        var productCompletedOn = await LoadCompletionDatesAsync(
            orderLines.Select(row => row.Order.Id).Distinct().ToArray(), cancellationToken);
        var recognized = orderLines
            .Where(row => groupSet == null || groupSet.Contains(row.Order.SalesGroupId))
            .Where(row =>
            {
                var recognitionDate = productCompletedOn.TryGetValue(row.Order.Id, out var completed) ? completed
                    : row.Payment?.DeliveredDate
                    ?? DateOnly.FromDateTime(row.Order.ServerReceivedAt.ToOffset(VietnamOffset).DateTime);
                return recognitionDate >= fromDate && recognitionDate <= toDate;
            })
            .ToArray();
        if (recognized.Length == 0) return [];

        // Real-time allocation writes the final per-line quantity and net amount onto the order
        // line itself, so the former approved-plan-version join is no longer needed.
        var normalized = recognized.Select(row => new
        {
            row.Order.Id,
            row.Line.ProductId,
            Sku = string.IsNullOrWhiteSpace(row.Line.SkuSnapshot) ? row.ProductSku : row.Line.SkuSnapshot,
            Name = string.IsNullOrWhiteSpace(row.Line.ProductNameSnapshot) ? row.ProductName : row.Line.ProductNameSnapshot,
            Quantity = row.Line.OrderedQuantity,
            LineNet = row.Line.NetAmount,
            AdjustedOrderAmount = row.Payment?.AdjustedAmount ?? row.Order.FinalAmount
        }).ToArray();

        var allocated = normalized.GroupBy(row => row.Id).SelectMany(orderGroup =>
        {
            var canonicalOrderNet = orderGroup.Sum(row => row.LineNet);
            return orderGroup.Select(row => new
            {
                row.ProductId, row.Sku, row.Name, row.Quantity,
                Revenue = canonicalOrderNet <= 0 ? 0 : decimal.Round(
                    row.AdjustedOrderAmount * row.LineNet / canonicalOrderNet, 2,
                    MidpointRounding.AwayFromZero)
            });
        });

        return allocated.GroupBy(row => new { row.ProductId, row.Sku, row.Name })
            .Select(group => new ProductSalesRow(
                group.Key.ProductId, group.Key.Sku, group.Key.Name,
                group.Sum(row => row.Quantity), group.Sum(row => row.Revenue)))
            .ToArray();
    }
}
