using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Picking;

public sealed class PickingPlanService(IPickingStore store) : IPickingPlanService
{
    public async Task CancelForOrderAsync(
        Guid orderId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var lists = await store.ListByOrderAsync(orderId, cancellationToken);
        if (lists.Any(item => item.Status != PickingListStatuses.Open))
            throw PickingOperationException.Conflict(
                "Order cannot be cancelled after warehouse picking has started.");
        var items = await store.ListItemsByOrderAsync(orderId, cancellationToken);
        foreach (var list in lists)
        {
            list.Status = PickingListStatuses.Cancelled;
            list.Version++;
            list.UpdatedAt = now;
        }
        foreach (var item in items) item.Status = PickingItemStatuses.Cancelled;
    }

    public async Task CreateForApprovedOrderAsync(
        OrderView order,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var allocations = order.Lines
            .SelectMany(line => line.Allocations.Select(allocation => new { line.Line, Allocation = allocation }))
            .OrderBy(item => item.Line.LineNumber)
            .ThenBy(item => item.Allocation.AllocationSequence)
            .ToArray();
        if (allocations.Length == 0)
            throw PickingOperationException.Conflict("Approved order has no allocation to pick.");
        if (allocations.Any(item => item.Allocation.Status != AllocationInventoryStatuses.Reserved))
            throw PickingOperationException.Conflict("Every allocation must be reserved before creating picking lists.");

        var groups = allocations.GroupBy(item => item.Allocation.WarehouseId)
            .OrderBy(group => order.WarehouseCodes[group.Key], StringComparer.Ordinal)
            .ToArray();
        if (groups.Length > 26)
            throw PickingOperationException.Conflict("An order cannot create more than 26 warehouse picking lists.");

        var existing = await store.ListByOrderAsync(order.Order.Id, cancellationToken);
        if (existing.Count > 0)
        {
            var expectedWarehouses = groups.Select(group => group.Key).Order().ToArray();
            var actualWarehouses = existing.Select(item => item.WarehouseId).Order().ToArray();
            if (!expectedWarehouses.SequenceEqual(actualWarehouses))
                throw PickingOperationException.Conflict("Existing picking lists do not match the approved allocation plan.");
            return;
        }

        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            var list = new PickingList
            {
                Id = Guid.NewGuid(),
                PickingCode = $"{order.Order.OrderCode}{(char)('A' + index)}",
                SalesOrderId = order.Order.Id,
                WarehouseId = group.Key,
                Status = PickingListStatuses.Open,
                Version = 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            store.AddList(list);
            foreach (var row in group)
            {
                store.AddItem(new PickingItem
                {
                    Id = Guid.NewGuid(),
                    PickingListId = list.Id,
                    AllocationId = row.Allocation.Id,
                    SourceBatchId = row.Allocation.BatchId,
                    RequestedQuantity = row.Allocation.AllocatedQuantity,
                    PickedQuantity = 0,
                    Status = PickingItemStatuses.Pending
                });
            }
        }
    }
}
