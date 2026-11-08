using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Refunds;
using FSCM.Application.Features.Shipments;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.AllocationRuns;

/// <summary>
/// Reads one run's whole story. Everything here is a read, so it is all untracked; the one write
/// (closing) takes the run again through <see cref="FindRunForUpdateAsync"/>.
/// </summary>
public sealed class AllocationRunReportStore(AppDbContext dbContext) : IAllocationRunReportStore
{
    /// <summary>Shipment statuses in which the goods have come to rest, one way or the other.</summary>
    private static readonly string[] SettledShipmentStatuses =
        [ShipmentStatuses.Received, ShipmentStatuses.Exception];

    /// <summary>Picking sheet statuses in which the warehouse has stopped working.</summary>
    private static readonly string[] FinishedSheetStatuses =
        [RunPickingListStatuses.Ready, RunPickingListStatuses.NeedsHandling];

    /// <summary>Refund statuses in which money is still owed.</summary>
    private static readonly string[] OpenRefundStatuses =
        [RefundStatuses.PendingRefund, RefundStatuses.AwaitingVerification];

    public async Task<AllocationRunReportView?> FindReportAsync(
        Guid runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.AllocationRunRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);
        if (run is null) return null;

        var orders = await ReadOrdersAsync(runId, cancellationToken);
        var stock = await ReadStockAsync(runId, cancellationToken);
        var sheetExceptions = await ReadSheetExceptionsAsync(runId, cancellationToken);
        var shipmentExceptions = await ReadShipmentExceptionsAsync(runId, cancellationToken);
        var orderIds = orders.Select(item => item.SalesOrderId).ToArray();

        return new AllocationRunReportView(
            run,
            orders,
            stock,
            [.. sheetExceptions, .. shipmentExceptions],
            [
                .. await ReadOpenComplaintsAsync(orderIds, cancellationToken),
                .. await ReadOpenRefundsAsync(orderIds, cancellationToken)
            ],
            await ReadUnfinishedSheetsAsync(runId, cancellationToken),
            await ReadShipmentsInFlightAsync(runId, cancellationToken),
            await CountStillHeldAsync(runId, cancellationToken),
            await ReadClosureAsync(runId, cancellationToken));
    }

    public Task<AllocationRun?> FindRunForUpdateAsync(Guid runId, CancellationToken cancellationToken) =>
        dbContext.AllocationRunRecords.SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);

    public void AddAuditLog(AuditLog log) => dbContext.AuditLogRecords.Add(log);

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await action(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public void ClearTracking() => dbContext.ChangeTracker.Clear();

    private async Task<IReadOnlyCollection<RunReportOrderRow>> ReadOrdersAsync(
        Guid runId, CancellationToken cancellationToken)
    {
        var query =
            from runOrder in dbContext.AllocationRunOrderRecords.AsNoTracking()
            where runOrder.AllocationRunId == runId
            join order in dbContext.SalesOrderRecords.AsNoTracking()
                on runOrder.SalesOrderId equals order.Id
            orderby runOrder.PriorityRank
            select new RunReportOrderRow(
                order.Id,
                runOrder.OrderCodeSnapshot,
                order.RetailerNameSnapshot,
                runOrder.PriorityRank,
                runOrder.Status,
                order.Status,
                runOrder.ShortageReason,
                order.FinalAmount);

        return await query.ToArrayAsync(cancellationToken);
    }

    /// <summary>
    /// What the plan drew from each batch, beside what became of it.
    ///
    /// The four reservation states are counted separately rather than summed into one "used"
    /// number: a quantity still held and a quantity that went out the door look the same on a
    /// total, and only one of them keeps the run open.
    /// </summary>
    private async Task<IReadOnlyCollection<RunReportStockRow>> ReadStockAsync(
        Guid runId, CancellationToken cancellationToken)
    {
        var query =
            from allocation in dbContext.AllocationRunAllocationRecords.AsNoTracking()
            where allocation.AllocationRunId == runId
            join batch in dbContext.BatchRecords.AsNoTracking() on allocation.BatchId equals batch.Id
            join product in dbContext.ProductRecords.AsNoTracking() on batch.ProductId equals product.Id
            join warehouse in dbContext.WarehouseRecords.AsNoTracking()
                on allocation.WarehouseId equals warehouse.Id
            select new
            {
                allocation.Id,
                allocation.WarehouseId,
                WarehouseCode = warehouse.Code,
                allocation.BatchId,
                batch.BatchCode,
                product.Sku,
                allocation.Quantity
            };

        var allocations = await query.ToArrayAsync(cancellationToken);
        if (allocations.Length == 0) return [];

        var allocationIds = allocations.Select(item => item.Id).ToArray();
        var reservations = await dbContext.ReservationRecords.AsNoTracking()
            .Where(item => item.AllocationRunAllocationId != null
                           && allocationIds.Contains(item.AllocationRunAllocationId!.Value))
            .Select(item => new HeldQuantity(
                item.AllocationRunAllocationId!.Value, item.Quantity, item.Status))
            .ToArrayAsync(cancellationToken);

        var byAllocation = reservations.GroupBy(item => item.AllocationId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        return allocations
            .GroupBy(item => (item.WarehouseId, item.BatchId))
            .Select(group =>
            {
                HeldQuantity[] rows =
                    [.. group.SelectMany(item => byAllocation.GetValueOrDefault(item.Id, []))];
                var first = group.First();
                return new RunReportStockRow(
                    first.WarehouseId,
                    first.WarehouseCode,
                    first.BatchId,
                    first.BatchCode,
                    first.Sku,
                    group.Sum(item => item.Quantity),
                    rows.Sum(item => item.Quantity),
                    Total(rows, ReservationStatuses.Consumed),
                    Total(rows, ReservationStatuses.Released),
                    Total(rows, ReservationStatuses.Active));
            })
            .OrderBy(row => row.WarehouseCode, StringComparer.Ordinal)
            .ThenBy(row => row.BatchCode, StringComparer.Ordinal)
            .ToArray();

        static decimal Total(HeldQuantity[] rows, string status) =>
            rows.Where(item => item.Status == status).Sum(item => item.Quantity);
    }

    /// <summary>One reservation's quantity and the state it is in, for summing per batch.</summary>
    private sealed record HeldQuantity(Guid AllocationId, decimal Quantity, string Status);

    private async Task<IReadOnlyCollection<RunReportException>> ReadSheetExceptionsAsync(
        Guid runId, CancellationToken cancellationToken)
    {
        var query =
            from sheet in dbContext.RunPickingListRecords.AsNoTracking()
            where sheet.AllocationRunId == runId
                  && (sheet.Status == RunPickingListStatuses.NeedsHandling || sheet.ExceptionType != null)
            select new RunReportException(
                RunExceptionKinds.PickingSheet,
                sheet.PickingCode,
                sheet.ExceptionType ?? sheet.Status,
                // A sheet flagged for handling with nothing written down is the exception nobody
                // has dealt with yet; a note on it is the record that it has been.
                sheet.ExceptionNote != null && sheet.ExceptionNote != "");

        return await query.ToArrayAsync(cancellationToken);
    }

    private async Task<IReadOnlyCollection<RunReportException>> ReadShipmentExceptionsAsync(
        Guid runId, CancellationToken cancellationToken)
    {
        var query =
            from shipment in dbContext.ShipmentRecords.AsNoTracking()
            join sheet in dbContext.RunPickingListRecords.AsNoTracking()
                on shipment.RunPickingListId equals sheet.Id
            where sheet.AllocationRunId == runId && shipment.Status == ShipmentStatuses.Exception
            select new RunReportException(
                RunExceptionKinds.Shipment,
                shipment.ShipmentCode,
                shipment.Status,
                shipment.ExceptionReason != null && shipment.ExceptionReason != "");

        return await query.ToArrayAsync(cancellationToken);
    }

    private async Task<IReadOnlyCollection<string>> ReadUnfinishedSheetsAsync(
        Guid runId, CancellationToken cancellationToken) =>
        await dbContext.RunPickingListRecords.AsNoTracking()
            .Where(item => item.AllocationRunId == runId
                           && !FinishedSheetStatuses.Contains(item.Status))
            .OrderBy(item => item.PickingCode)
            .Select(item => item.PickingCode)
            .ToArrayAsync(cancellationToken);

    private async Task<IReadOnlyCollection<string>> ReadShipmentsInFlightAsync(
        Guid runId, CancellationToken cancellationToken)
    {
        var query =
            from shipment in dbContext.ShipmentRecords.AsNoTracking()
            join sheet in dbContext.RunPickingListRecords.AsNoTracking()
                on shipment.RunPickingListId equals sheet.Id
            where sheet.AllocationRunId == runId
                  && !SettledShipmentStatuses.Contains(shipment.Status)
            orderby shipment.ShipmentCode
            select shipment.ShipmentCode;

        return await query.ToArrayAsync(cancellationToken);
    }

    private Task<int> CountStillHeldAsync(Guid runId, CancellationToken cancellationToken)
    {
        var query =
            from allocation in dbContext.AllocationRunAllocationRecords.AsNoTracking()
            where allocation.AllocationRunId == runId
            join reservation in dbContext.ReservationRecords.AsNoTracking()
                on allocation.Id equals reservation.AllocationRunAllocationId
            where reservation.Status == ReservationStatuses.Active
            select reservation.Id;

        return query.CountAsync(cancellationToken);
    }

    /// <summary>
    /// Complaints still open on the run's orders. Open is <c>ResolvedAt is null</c> rather than a
    /// status name: three different statuses close one, and a missing resolution time is the fact
    /// they all share.
    /// </summary>
    private async Task<IReadOnlyCollection<RunReportObligation>> ReadOpenComplaintsAsync(
        Guid[] orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Length == 0) return [];
        var query =
            from complaint in dbContext.ReceiptDiscrepancyRecords.AsNoTracking()
            where orderIds.Contains(complaint.SalesOrderId) && complaint.ResolvedAt == null
            join order in dbContext.SalesOrderRecords.AsNoTracking()
                on complaint.SalesOrderId equals order.Id
            select new RunReportObligation(
                RunObligationKinds.Complaint,
                complaint.Id.ToString(),
                order.OrderCode,
                null,
                complaint.Status);

        return await query.ToArrayAsync(cancellationToken);
    }

    private async Task<IReadOnlyCollection<RunReportObligation>> ReadOpenRefundsAsync(
        Guid[] orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Length == 0) return [];
        var query =
            from refund in dbContext.ManualRefundRecords.AsNoTracking()
            where orderIds.Contains(refund.SalesOrderId) && OpenRefundStatuses.Contains(refund.Status)
            join order in dbContext.SalesOrderRecords.AsNoTracking()
                on refund.SalesOrderId equals order.Id
            select new RunReportObligation(
                RunObligationKinds.Refund,
                refund.Id.ToString(),
                order.OrderCode,
                refund.Amount,
                refund.Status);

        return await query.ToArrayAsync(cancellationToken);
    }

    private Task<AuditLog?> ReadClosureAsync(Guid runId, CancellationToken cancellationToken) =>
        dbContext.AuditLogRecords.AsNoTracking()
            .Where(item => item.EntityName == AllocationRunClosureService.ClosureEntityName
                           && item.EntityId == runId
                           && item.Action == AllocationRunClosureService.ClosureAction)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
