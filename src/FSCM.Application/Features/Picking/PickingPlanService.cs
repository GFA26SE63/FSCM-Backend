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

}
