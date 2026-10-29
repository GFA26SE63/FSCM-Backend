using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Orders;

/// <summary>
/// Reads what an order is holding, by way of the run that holds it.
///
/// Everything is read tracked: the caller is inside the cancellation's transaction and mutates
/// these rows, and <c>AsNoTracking</c> here would make every release a silent no-op.
/// </summary>
public sealed class OrderReleaseStore(AppDbContext context) : IOrderReleaseStore
{
    public async Task<IReadOnlyCollection<OrderHeldAllocation>> ListHeldAllocationsAsync(
        Guid salesOrderId, CancellationToken cancellationToken)
    {
        // run order -> run line -> allocation is the only path from an order to the stock it holds;
        // the per-order Allocation table is not written any more.
        var query =
            from runOrder in context.AllocationRunOrderRecords
            where runOrder.SalesOrderId == salesOrderId
            join runLine in context.AllocationRunLineRecords
                on runOrder.Id equals runLine.AllocationRunOrderId
            join allocation in context.AllocationRunAllocationRecords
                on runLine.Id equals allocation.AllocationRunLineId
            join reservation in context.ReservationRecords
                on allocation.Id equals reservation.AllocationRunAllocationId
            join batch in context.BatchRecords on allocation.BatchId equals batch.Id
            join balance in context.InventoryBalanceRecords on batch.Id equals balance.BatchId
            where reservation.Status == ReservationStatuses.Active
            select new OrderHeldAllocation(allocation.Id, reservation, balance, batch);

        return await query.ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<OrderPickingShare>> ListPickingSharesAsync(
        Guid salesOrderId, CancellationToken cancellationToken)
    {
        var query =
            from split in context.RunPickingSplitRecords
            where split.SalesOrderId == salesOrderId
            join gather in context.RunPickingGatherRecords
                on split.RunPickingGatherId equals gather.Id
            join list in context.RunPickingListRecords
                on gather.RunPickingListId equals list.Id
            select new OrderPickingShare(split, gather, list);

        return await query.ToArrayAsync(cancellationToken);
    }

    public void RemoveSplit(RunPickingSplit split) => context.RunPickingSplitRecords.Remove(split);

    public void RemoveGather(RunPickingGather gather) => context.RunPickingGatherRecords.Remove(gather);

    public void AddInventoryTransaction(InventoryTransaction transaction) =>
        context.InventoryTransactionRecords.Add(transaction);
}
