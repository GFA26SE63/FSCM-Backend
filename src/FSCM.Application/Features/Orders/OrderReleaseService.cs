using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Inventory;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Orders;

public sealed record OrderReleaseOutcome(
    int ReleasedAllocationCount,
    decimal ReleasedQuantity,
    int RemovedPickingRowCount);

/// <summary>
/// Gives back everything an order was holding, so a cancelled order stops standing between live
/// stock and the next run (F94).
///
/// Two models can hold stock in this schema: the older per-order <c>Allocation</c> and an
/// allocation made by a daily run. Nothing writes the older one any more - every order that goes
/// through a run holds its stock through <c>AllocationRunAllocation</c> instead - so a release that
/// only walks the order's own allocation rows walks an empty list and silently frees nothing. That
/// is what this service exists to do properly.
///
/// Releasing has to be part of the same transaction as the cancellation. An order marked cancelled
/// whose stock is still held is worse than either half alone: the quantity is invisible to the next
/// run and no screen shows an order that could explain it.
/// </summary>
public sealed class OrderReleaseService(IOrderReleaseStore store, TimeProvider timeProvider)
{
    /// <summary>
    /// Releases the order's held stock and takes its rows off any warehouse picking sheet.
    ///
    /// Refuses once a warehouse has started on it. F94 allows cancelling after the stock was held
    /// and before picking; past that point someone is standing at a shelf with a sheet in hand, and
    /// quietly deleting their rows under them is how a picked carton goes missing.
    ///
    /// Safe to call on an order holding nothing: an order cancelled before it ever reached a run,
    /// or a cancellation sent twice, releases nothing and reports zero.
    /// </summary>
    public async Task<OrderReleaseOutcome> ReleaseAsync(
        Guid salesOrderId,
        Guid actorUserId,
        string referenceCode,
        CancellationToken cancellationToken)
    {
        var removed = await RemoveFromPickingSheetsAsync(salesOrderId, cancellationToken);
        var held = await store.ListHeldAllocationsAsync(salesOrderId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var quantity = 0m;

        foreach (var row in held)
        {
            var balance = row.Balance;
            var amount = row.Reservation.Quantity;
            if (balance.ReservedQuantity < amount)
            {
                // The held quantity and the balance disagree, which no sequence of releases should
                // produce. Writing a negative reservation would hide it, so stop and say so.
                throw OrderOperationException.Conflict(
                    $"Batch {row.Batch.BatchCode} holds {balance.ReservedQuantity:0.###} reserved " +
                    $"but this order alone is holding {amount:0.###}.");
            }

            balance.ReservedQuantity -= amount;
            balance.AvailableQuantity = balance.PhysicalQuantity - balance.ReservedQuantity;
            balance.Version++;
            balance.UpdatedAt = now;

            row.Reservation.Status = ReservationStatuses.Released;
            row.Reservation.ReleasedAt = now;

            store.AddInventoryTransaction(new InventoryTransaction
            {
                Id = Guid.NewGuid(),
                BatchId = row.Batch.Id,
                WarehouseId = row.Batch.WarehouseId,
                ProductId = row.Batch.ProductId,
                CreatedByUserId = actorUserId,
                TransactionType = InventoryTransactionTypes.OrderRelease,
                SourceType = InventoryTransactionSourceTypes.Allocation,
                SourceId = row.AllocationRunAllocationId,
                ReferenceCode = referenceCode,
                // Letting go does not move anything: only the reserved side changes.
                PhysicalQuantityDelta = 0m,
                ReservedQuantityDelta = -amount,
                PhysicalQuantityAfter = balance.PhysicalQuantity,
                ReservedQuantityAfter = balance.ReservedQuantity,
                OccurredAt = now
            });

            quantity += amount;
        }

        return new OrderReleaseOutcome(held.Count, quantity, removed);
    }

    /// <summary>
    /// Takes the order's rows off the sheets. A sheet belongs to the whole run, so the gather row
    /// it shares with other orders is reduced by this order's share rather than removed, and only
    /// a gather left with nothing to collect goes away.
    /// </summary>
    private async Task<int> RemoveFromPickingSheetsAsync(
        Guid salesOrderId, CancellationToken cancellationToken)
    {
        var shares = await store.ListPickingSharesAsync(salesOrderId, cancellationToken);
        if (shares.Count == 0) return 0;

        var started = shares.FirstOrDefault(item =>
            item.Split.PickedQuantity > 0 || item.List.Status != RunPickingListStatuses.Issued);
        if (started is not null)
        {
            throw OrderOperationException.Conflict(
                $"Warehouse picking has started on sheet {started.List.PickingCode}; " +
                "this order can no longer be cancelled here.");
        }

        foreach (var group in shares.GroupBy(item => item.Gather.Id))
        {
            var gather = group.First().Gather;
            foreach (var share in group)
            {
                gather.RequiredQuantity -= share.Split.Quantity;
                store.RemoveSplit(share.Split);
            }

            // Nothing left for this batch once this order is out of it, so the trip to the shelf
            // should not stay on the sheet at all.
            if (gather.RequiredQuantity <= 0)
            {
                store.RemoveGather(gather);
            }
        }

        return shares.Count;
    }
}
