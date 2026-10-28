using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Orders;

public sealed class OrderReleaseServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherOrderId = Guid.Parse("11111111-1111-1111-1111-111111111112");
    private static readonly Guid ActorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BatchId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Releasing_gives_the_held_quantity_back_to_available()
    {
        var (service, store) = Build();
        var balance = Hold(store, 20m, physical: 100m, reserved: 20m);

        var outcome = await service.ReleaseAsync(OrderId, ActorId, "DH-001", CancellationToken.None);

        // The point of the whole service: stock the order was holding is stock the next run can use.
        Assert.Equal(1, outcome.ReleasedAllocationCount);
        Assert.Equal(20m, outcome.ReleasedQuantity);
        Assert.Equal(0m, balance.ReservedQuantity);
        Assert.Equal(100m, balance.AvailableQuantity);
    }

    [Fact]
    public async Task Releasing_leaves_a_trail_in_the_stock_ledger()
    {
        var (service, store) = Build();
        var balance = Hold(store, 20m, physical: 100m, reserved: 20m);

        await service.ReleaseAsync(OrderId, ActorId, "DH-001", CancellationToken.None);

        var row = Assert.Single(store.Transactions);
        Assert.Equal(InventoryTransactionTypes.OrderRelease, row.TransactionType);
        // Letting go moves nothing off the shelf: only the reserved side changes.
        Assert.Equal(0m, row.PhysicalQuantityDelta);
        Assert.Equal(-20m, row.ReservedQuantityDelta);
        Assert.Equal(balance.PhysicalQuantity, row.PhysicalQuantityAfter);
        Assert.Equal(0m, row.ReservedQuantityAfter);
        Assert.Equal("DH-001", row.ReferenceCode);
    }

    [Fact]
    public async Task An_order_holding_nothing_releases_nothing_and_is_not_an_error()
    {
        var (service, store) = Build();

        var outcome = await service.ReleaseAsync(OrderId, ActorId, "DH-001", CancellationToken.None);

        // An order cancelled before it ever reached a run, and a cancellation sent twice, both
        // arrive here. Neither is a problem, and neither may take the cancellation down with it.
        Assert.Equal(0, outcome.ReleasedAllocationCount);
        Assert.Equal(0m, outcome.ReleasedQuantity);
        Assert.Empty(store.Transactions);
    }

    [Fact]
    public async Task A_reservation_already_let_go_of_is_not_released_twice()
    {
        var (service, store) = Build();
        var balance = Hold(store, 20m, physical: 100m, reserved: 20m);
        await service.ReleaseAsync(OrderId, ActorId, "DH-001", CancellationToken.None);

        // The store only ever hands back active reservations, so the second pass sees nothing.
        store.Held.RemoveAll(item => item.Reservation.Status == ReservationStatuses.Released);
        var again = await service.ReleaseAsync(OrderId, ActorId, "DH-001", CancellationToken.None);

        Assert.Equal(0, again.ReleasedAllocationCount);
        Assert.Equal(0m, balance.ReservedQuantity);
        Assert.Single(store.Transactions);
    }

    [Fact]
    public async Task A_balance_holding_less_than_the_order_claims_is_refused_rather_than_driven_negative()
    {
        var (service, store) = Build();
        Hold(store, 20m, physical: 100m, reserved: 5m);

        var error = await Assert.ThrowsAsync<OrderOperationException>(() =>
            service.ReleaseAsync(OrderId, ActorId, "DH-001", CancellationToken.None));

        // No sequence of releases produces this. A negative reservation would bury it.
        Assert.Equal("conflict", error.Code);
    }

    [Fact]
    public async Task Cancelling_takes_the_order_off_the_sheet_and_leaves_the_other_orders_on_it()
    {
        var (service, store) = Build();
        // One trip to the batch collects 30: 20 for this order, 10 for somebody else's.
        var gather = NewGather(required: 30m);
        var list = NewList(RunPickingListStatuses.Issued);
        store.Shares.Add(new OrderPickingShare(NewSplit(OrderId, 20m), gather, list));
        store.Shares.Add(new OrderPickingShare(NewSplit(OtherOrderId, 10m), gather, list));

        var outcome = await service.ReleaseAsync(OrderId, ActorId, "DH-001", CancellationToken.None);

        // The sheet belongs to the whole run: the trip stays, shortened by what this order wanted.
        Assert.Equal(1, outcome.RemovedPickingRowCount);
        Assert.Equal(10m, gather.RequiredQuantity);
        Assert.Empty(store.RemovedGathers);
    }

    [Fact]
    public async Task A_trip_that_exists_only_for_this_order_leaves_the_sheet_entirely()
    {
        var (service, store) = Build();
        var gather = NewGather(required: 20m);
        store.Shares.Add(new OrderPickingShare(
            NewSplit(OrderId, 20m), gather, NewList(RunPickingListStatuses.Issued)));

        await service.ReleaseAsync(OrderId, ActorId, "DH-001", CancellationToken.None);

        // Nothing left to collect from that batch, so sending someone to the shelf for zero cartons
        // is worse than not listing it at all.
        Assert.Equal(gather, Assert.Single(store.RemovedGathers));
        Assert.Single(store.RemovedSplits);
    }

    [Theory]
    [InlineData(RunPickingListStatuses.Picking, 0)]
    [InlineData(RunPickingListStatuses.Ready, 0)]
    [InlineData(RunPickingListStatuses.Issued, 5)]
    public async Task Once_a_warehouse_has_started_the_order_can_no_longer_be_pulled_out(
        string listStatus, decimal pickedQuantity)
    {
        var (service, store) = Build();
        var split = NewSplit(OrderId, 20m);
        split.PickedQuantity = pickedQuantity;
        store.Shares.Add(new OrderPickingShare(split, NewGather(required: 20m), NewList(listStatus)));

        var error = await Assert.ThrowsAsync<OrderOperationException>(() =>
            service.ReleaseAsync(OrderId, ActorId, "DH-001", CancellationToken.None));

        // F94 allows cancelling after the stock was held and before picking. Past that someone is
        // standing at a shelf with this sheet in hand.
        Assert.Equal("conflict", error.Code);
        Assert.Empty(store.RemovedSplits);
    }

    [Fact]
    public async Task Nothing_is_released_when_the_sheet_refuses_the_cancellation()
    {
        var (service, store) = Build();
        var balance = Hold(store, 20m, physical: 100m, reserved: 20m);
        store.Shares.Add(new OrderPickingShare(
            NewSplit(OrderId, 20m), NewGather(20m), NewList(RunPickingListStatuses.Picking)));

        await Assert.ThrowsAsync<OrderOperationException>(() =>
            service.ReleaseAsync(OrderId, ActorId, "DH-001", CancellationToken.None));

        // The sheet is checked before any stock moves, so a refused cancellation leaves the hold
        // exactly as it was rather than freeing stock for an order that is still live.
        Assert.Equal(20m, balance.ReservedQuantity);
        Assert.Empty(store.Transactions);
    }

    private static (OrderReleaseService Service, FakeOrderReleaseStore Store) Build()
    {
        var store = new FakeOrderReleaseStore();
        return (new OrderReleaseService(store, new FixedClock()), store);
    }

    /// <summary>Registers one allocation of the order holding <paramref name="quantity"/>.</summary>
    private static InventoryBalance Hold(
        FakeOrderReleaseStore store, decimal quantity, decimal physical, decimal reserved)
    {
        var balance = new InventoryBalance
        {
            BatchId = BatchId,
            PhysicalQuantity = physical,
            ReservedQuantity = reserved,
            AvailableQuantity = physical - reserved,
            Version = 1
        };
        store.Held.Add(new OrderHeldAllocation(
            Guid.NewGuid(),
            new Reservation
            {
                Id = Guid.NewGuid(),
                BatchId = BatchId,
                Quantity = quantity,
                Status = ReservationStatuses.Active,
                ReservedAt = Now
            },
            balance,
            new Batch { Id = BatchId, BatchCode = "B-001", WarehouseId = Guid.NewGuid(), ProductId = Guid.NewGuid() }));
        return balance;
    }

    private static RunPickingSplit NewSplit(Guid orderId, decimal quantity) => new()
    {
        Id = Guid.NewGuid(),
        SalesOrderId = orderId,
        Quantity = quantity,
        PickedQuantity = 0
    };

    private static RunPickingGather NewGather(decimal required) => new()
    {
        Id = Guid.NewGuid(),
        BatchId = BatchId,
        RequiredQuantity = required,
        Status = RunPickingGatherStatuses.Pending
    };

    private static RunPickingList NewList(string status) => new()
    {
        Id = Guid.NewGuid(),
        PickingCode = "PK-001A",
        Status = status,
        IssuedAt = Now
    };

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
