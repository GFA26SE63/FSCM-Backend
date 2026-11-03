using FSCM.Application.Features.Orders;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Orders;

public sealed class FakeOrderReleaseStore : IOrderReleaseStore
{
    public List<OrderHeldAllocation> Held { get; } = [];
    public List<OrderPickingShare> Shares { get; } = [];
    public List<PickingItem> RemovedItems { get; } = [];
    public List<InventoryTransaction> Transactions { get; } = [];

    public Task<IReadOnlyCollection<OrderHeldAllocation>> ListHeldAllocationsAsync(
        Guid salesOrderId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<OrderHeldAllocation>>(
            Held.Where(item => item.Reservation.Status == "Active").ToArray());

    public Task<IReadOnlyCollection<OrderPickingShare>> ListPickingSharesAsync(
        Guid salesOrderId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<OrderPickingShare>>(Shares.ToArray());

    public Task<int> CountPickingItemsAsync(Guid pickingListId, CancellationToken cancellationToken) =>
        Task.FromResult(Shares.Count(item => item.List.Id == pickingListId));

    public void RemovePickingItem(PickingItem item)
    {
        RemovedItems.Add(item);
        Shares.RemoveAll(row => row.Item.Id == item.Id);
    }

    public void AddInventoryTransaction(InventoryTransaction transaction) => Transactions.Add(transaction);
}
