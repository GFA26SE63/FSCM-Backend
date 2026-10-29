using FSCM.Application.Features.Orders;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Orders;

/// <summary>
/// In-memory stand-in for what an order is holding, and a no-op for the order tests that merely
/// have to hand one to <see cref="OrderReleaseService"/>.
///
/// With nothing registered the release finds nothing and reports zero, which is exactly what an
/// order cancelled before it ever reached a run does.
/// </summary>
public sealed class FakeOrderReleaseStore : IOrderReleaseStore
{
    public List<OrderHeldAllocation> Held { get; } = [];

    public List<OrderPickingShare> Shares { get; } = [];

    public List<RunPickingSplit> RemovedSplits { get; } = [];

    public List<RunPickingGather> RemovedGathers { get; } = [];

    public List<InventoryTransaction> Transactions { get; } = [];

    /// <summary>Everything registered in <see cref="Held"/> belongs to the order under test.</summary>
    public Task<IReadOnlyCollection<OrderHeldAllocation>> ListHeldAllocationsAsync(
        Guid salesOrderId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<OrderHeldAllocation>>(Held.ToArray());

    /// <summary>
    /// Only this order's rows, the way the real store's join gives them. A sheet is shared by every
    /// order in the run, so handing back all of them would let a release subtract somebody else's
    /// cartons from the trip.
    /// </summary>
    public Task<IReadOnlyCollection<OrderPickingShare>> ListPickingSharesAsync(
        Guid salesOrderId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<OrderPickingShare>>(
            Shares.Where(item => item.Split.SalesOrderId == salesOrderId).ToArray());

    public void RemoveSplit(RunPickingSplit split)
    {
        RemovedSplits.Add(split);
        Shares.RemoveAll(item => item.Split.Id == split.Id);
    }

    public void RemoveGather(RunPickingGather gather) => RemovedGathers.Add(gather);

    public void AddInventoryTransaction(InventoryTransaction transaction) => Transactions.Add(transaction);
}
