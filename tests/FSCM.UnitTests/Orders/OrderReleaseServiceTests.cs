using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Picking;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Orders;

public sealed class OrderReleaseServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BatchId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Releasing_gives_the_held_quantity_back_to_available()
    {
        var (service, store) = Build();
        var balance = Hold(store, 20m, 100m, 20m);
        var outcome = await service.ReleaseAsync(OrderId, ActorId, "DH-001", default);
        Assert.Equal(1, outcome.ReleasedAllocationCount);
        Assert.Equal(20m, outcome.ReleasedQuantity);
        Assert.Equal(0m, balance.ReservedQuantity);
        Assert.Equal(100m, balance.AvailableQuantity);
        Assert.Equal(AllocationInventoryStatuses.Released, store.Held.Single().Allocation.Status);
    }

    [Fact]
    public async Task Releasing_leaves_a_stock_ledger_entry()
    {
        var (service, store) = Build();
        Hold(store, 20m, 100m, 20m);
        await service.ReleaseAsync(OrderId, ActorId, "DH-001", default);
        var row = Assert.Single(store.Transactions);
        Assert.Equal(InventoryTransactionTypes.OrderRelease, row.TransactionType);
        Assert.Equal(0m, row.PhysicalQuantityDelta);
        Assert.Equal(-20m, row.ReservedQuantityDelta);
        Assert.Equal("DH-001", row.ReferenceCode);
    }

    [Fact]
    public async Task Repeating_a_release_is_idempotent()
    {
        var (service, store) = Build();
        Hold(store, 20m, 100m, 20m);
        await service.ReleaseAsync(OrderId, ActorId, "DH-001", default);
        var again = await service.ReleaseAsync(OrderId, ActorId, "DH-001", default);
        Assert.Equal(0, again.ReleasedAllocationCount);
        Assert.Single(store.Transactions);
    }

    [Fact]
    public async Task Inconsistent_reserved_quantity_is_refused()
    {
        var (service, store) = Build();
        Hold(store, 20m, 100m, 5m);
        var error = await Assert.ThrowsAsync<OrderOperationException>(() =>
            service.ReleaseAsync(OrderId, ActorId, "DH-001", default));
        Assert.Equal("conflict", error.Code);
    }

    [Fact]
    public async Task Pending_picking_item_is_removed_and_empty_list_is_cancelled()
    {
        var (service, store) = Build();
        var list = NewList(PickingListStatuses.Pending);
        store.Shares.Add(new OrderPickingShare(NewItem(0), list));
        var outcome = await service.ReleaseAsync(OrderId, ActorId, "DH-001", default);
        Assert.Equal(1, outcome.RemovedPickingRowCount);
        Assert.Single(store.RemovedItems);
        Assert.Equal(PickingListStatuses.Cancelled, list.Status);
    }

    [Theory]
    [InlineData(PickingListStatuses.InProgress, 0)]
    [InlineData(PickingListStatuses.Completed, 0)]
    [InlineData(PickingListStatuses.Pending, 5)]
    public async Task Started_picking_prevents_release(string status, decimal picked)
    {
        var (service, store) = Build();
        store.Shares.Add(new OrderPickingShare(NewItem(picked), NewList(status)));
        var error = await Assert.ThrowsAsync<OrderOperationException>(() =>
            service.ReleaseAsync(OrderId, ActorId, "DH-001", default));
        Assert.Equal("conflict", error.Code);
        Assert.Empty(store.RemovedItems);
    }

    [Fact]
    public async Task Refused_picking_release_does_not_change_inventory()
    {
        var (service, store) = Build();
        var balance = Hold(store, 20m, 100m, 20m);
        store.Shares.Add(new OrderPickingShare(NewItem(0), NewList(PickingListStatuses.InProgress)));
        await Assert.ThrowsAsync<OrderOperationException>(() =>
            service.ReleaseAsync(OrderId, ActorId, "DH-001", default));
        Assert.Equal(20m, balance.ReservedQuantity);
        Assert.Empty(store.Transactions);
    }

    private static (OrderReleaseService Service, FakeOrderReleaseStore Store) Build()
    {
        var store = new FakeOrderReleaseStore();
        return (new OrderReleaseService(store, new FixedClock()), store);
    }

    private static InventoryBalance Hold(FakeOrderReleaseStore store, decimal quantity,
        decimal physical, decimal reserved)
    {
        var allocation = new FSCM.Domain.Entities.Allocation
        {
            Id = Guid.NewGuid(), BatchId = BatchId, Status = AllocationInventoryStatuses.Reserved
        };
        var balance = new InventoryBalance
        {
            BatchId = BatchId, PhysicalQuantity = physical, ReservedQuantity = reserved,
            AvailableQuantity = physical - reserved, Version = 1
        };
        store.Held.Add(new OrderHeldAllocation(allocation,
            new Reservation
            {
                Id = Guid.NewGuid(), AllocationId = allocation.Id, BatchId = BatchId,
                Quantity = quantity, Status = ReservationStatuses.Active, ReservedAt = Now
            }, balance,
            new Batch
            {
                Id = BatchId, BatchCode = "B-001", WarehouseId = Guid.NewGuid(),
                ProductId = Guid.NewGuid()
            }));
        return balance;
    }

    private static PickingItem NewItem(decimal picked) => new()
    {
        Id = Guid.NewGuid(), RequestedQuantity = 20, PickedQuantity = picked,
        Status = PickingItemStatuses.Pending
    };

    private static PickingList NewList(string status) => new()
    {
        Id = Guid.NewGuid(), PickingCode = "PK-001A", Status = status
    };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
