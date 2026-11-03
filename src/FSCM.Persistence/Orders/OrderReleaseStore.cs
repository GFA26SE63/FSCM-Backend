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
        var query =
            from line in context.SalesOrderLineRecords
            where line.SalesOrderId == salesOrderId
            join allocation in context.AllocationRecords
                on line.Id equals allocation.SalesOrderLineId
            join reservation in context.ReservationRecords
                on allocation.Id equals reservation.AllocationId
            join batch in context.BatchRecords on allocation.BatchId equals batch.Id
            join balance in context.InventoryBalanceRecords on batch.Id equals balance.BatchId
            where reservation.Status == ReservationStatuses.Active
            select new OrderHeldAllocation(allocation, reservation, balance, batch);

        return await query.ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<OrderPickingShare>> ListPickingSharesAsync(
        Guid salesOrderId, CancellationToken cancellationToken)
    {
        var query =
            from line in context.SalesOrderLineRecords
            where line.SalesOrderId == salesOrderId
            join allocation in context.AllocationRecords on line.Id equals allocation.SalesOrderLineId
            join item in context.PickingItemRecords on allocation.Id equals item.AllocationId
            join list in context.PickingListRecords on item.PickingListId equals list.Id
            select new OrderPickingShare(item, list);

        return await query.ToArrayAsync(cancellationToken);
    }

    public Task<int> CountPickingItemsAsync(Guid pickingListId, CancellationToken cancellationToken) =>
        context.PickingItemRecords.CountAsync(item => item.PickingListId == pickingListId, cancellationToken);

    public void RemovePickingItem(PickingItem item) => context.PickingItemRecords.Remove(item);

    public void AddInventoryTransaction(InventoryTransaction transaction) =>
        context.InventoryTransactionRecords.Add(transaction);
}
