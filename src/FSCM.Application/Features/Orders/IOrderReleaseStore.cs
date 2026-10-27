using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Orders;

/// <summary>One allocation of an order that is currently holding stock, with everything needed to let go of it.</summary>
public sealed record OrderHeldAllocation(
    Guid AllocationRunAllocationId,
    Reservation Reservation,
    InventoryBalance Balance,
    Batch Batch);

/// <summary>
/// One order's share of a warehouse picking sheet. The sheet belongs to the whole run, so a
/// cancellation touches these rows and leaves the other orders on it alone.
/// </summary>
public sealed record OrderPickingShare(
    RunPickingSplit Split,
    RunPickingGather Gather,
    RunPickingList List);

public interface IOrderReleaseStore
{
    /// <summary>
    /// The order's allocations that still hold stock, through the run model. Only active
    /// reservations are returned: one already released or consumed is not ours to touch.
    /// </summary>
    Task<IReadOnlyCollection<OrderHeldAllocation>> ListHeldAllocationsAsync(
        Guid salesOrderId, CancellationToken cancellationToken);

    /// <summary>The order's rows on any run picking sheet, issued or in progress.</summary>
    Task<IReadOnlyCollection<OrderPickingShare>> ListPickingSharesAsync(
        Guid salesOrderId, CancellationToken cancellationToken);

    void RemoveSplit(RunPickingSplit split);

    void RemoveGather(RunPickingGather gather);

    void AddInventoryTransaction(InventoryTransaction transaction);
}
