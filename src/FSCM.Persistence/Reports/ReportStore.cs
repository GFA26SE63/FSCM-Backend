using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Reports;
using FSCM.Persistence.DbContexts;
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

    public async Task<IReadOnlyCollection<SalesRevenueRow>> AggregateSalesRevenueAsync(
        DateOnly fromDate,
        DateOnly toDate,
        IReadOnlyCollection<Guid>? salesGroupIds,
        CancellationToken cancellationToken)
    {
        // Delivered orders are the recognized-revenue source (same as the KPI, F75). The recognized
        // amount is the F64-adjusted payment amount when a ledger exists, else the post-promotion
        // order total. The recognition date is the payment delivered date, else the order's
        // Vietnam-local received date — both bounded to the inclusive [fromDate, toDate] window.
        var groupSet = salesGroupIds?.ToHashSet();

        var rows = await (
            from order in dbContext.SalesOrderRecords.AsNoTracking()
            where order.Status == OrderStatuses.Delivered
            join payment in dbContext.PaymentRecords.AsNoTracking()
                on order.Id equals payment.SalesOrderId into payments
            from payment in payments.DefaultIfEmpty()
            join user in dbContext.UserAccountRecords.AsNoTracking()
                on order.CreatedByUserId equals user.Id
            join salesGroup in dbContext.SalesGroupRecords.AsNoTracking()
                on order.SalesGroupId equals salesGroup.Id
            select new
            {
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

        var filtered = rows
            .Where(row => groupSet == null || groupSet.Contains(row.SalesGroupId))
            .Where(row =>
            {
                var recognitionDate = row.DeliveredDate
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
}
