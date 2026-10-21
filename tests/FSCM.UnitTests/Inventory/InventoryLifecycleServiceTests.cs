using FSCM.Application.Features.Inventory;
using FSCM.Domain.Entities;
using InventoryAllocation = FSCM.Domain.Entities.Allocation;

namespace FSCM.UnitTests.Inventory;

public sealed class InventoryLifecycleServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Lifecycle_reserves_then_issues_without_held_balance()
    {
        var fixture = new Fixture(physicalQuantity: 20, allocationQuantity: 8);

        var reserved = await fixture.Service.ReserveAsync(fixture.Allocation.Id, fixture.ActorId, 0, default);
        var issued = await fixture.Service.IssueAsync(fixture.Allocation.Id, fixture.ActorId, reserved.Version, default);

        Assert.Equal((20m, 8m, 12m),
            (reserved.OnHandQuantity, reserved.ReservedQuantity, reserved.AvailableQuantity));
        Assert.Equal((12m, 0m, 12m),
            (issued.OnHandQuantity, issued.ReservedQuantity, issued.AvailableQuantity));
        Assert.Equal(AllocationInventoryStatuses.Issued, issued.Status);
        Assert.Equal(2, issued.Version);
        Assert.Equal(2, fixture.Store.Transactions.Count);
        Assert.All(fixture.Store.Transactions, transaction =>
        {
            Assert.Equal(InventoryTransactionSourceTypes.Allocation, transaction.SourceType);
            Assert.Equal(fixture.Allocation.Id, transaction.SourceId);
        });
    }

    [Fact]
    public async Task Reserve_rejects_over_allocation_without_mutating_inventory()
    {
        var fixture = new Fixture(physicalQuantity: 5, allocationQuantity: 6);

        var exception = await Assert.ThrowsAsync<InventoryOperationException>(() =>
            fixture.Service.ReserveAsync(fixture.Allocation.Id, fixture.ActorId, 0, default));

        Assert.Equal("conflict", exception.Code);
        Assert.Equal(5, fixture.Balance.AvailableQuantity);
        Assert.Empty(fixture.Store.Reservations);
        Assert.Empty(fixture.Store.Transactions);
    }

    [Fact]
    public async Task Repeating_reserve_and_issue_is_idempotent()
    {
        var fixture = new Fixture(physicalQuantity: 20, allocationQuantity: 5);

        var reserved = await fixture.Service.ReserveAsync(fixture.Allocation.Id, fixture.ActorId, 0, default);
        var reservedAgain = await fixture.Service.ReserveAsync(fixture.Allocation.Id, fixture.ActorId, 0, default);
        var issued = await fixture.Service.IssueAsync(fixture.Allocation.Id, fixture.ActorId, reserved.Version, default);
        var issuedAgain = await fixture.Service.IssueAsync(fixture.Allocation.Id, fixture.ActorId, reserved.Version, default);

        Assert.Equal(reserved.Version, reservedAgain.Version);
        Assert.Equal(issued.Version, issuedAgain.Version);
        Assert.Single(fixture.Store.Reservations);
        Assert.Equal(2, fixture.Store.Transactions.Count);
    }

    [Fact]
    public async Task Release_returns_reserved_stock_once()
    {
        var fixture = new Fixture(physicalQuantity: 20, allocationQuantity: 7);
        var reserved = await fixture.Service.ReserveAsync(fixture.Allocation.Id, fixture.ActorId, 0, default);

        var released = await fixture.Service.ReleaseAsync(
            fixture.Allocation.Id, fixture.ActorId, reserved.Version, default);
        var releasedAgain = await fixture.Service.ReleaseAsync(
            fixture.Allocation.Id, fixture.ActorId, reserved.Version, default);

        Assert.Equal((20m, 0m, 20m),
            (released.OnHandQuantity, released.ReservedQuantity, released.AvailableQuantity));
        Assert.Equal(AllocationInventoryStatuses.Released, released.Status);
        Assert.Equal(released.Version, releasedAgain.Version);
    }

    [Fact]
    public async Task Release_before_reservation_does_not_change_inventory()
    {
        var fixture = new Fixture(physicalQuantity: 20, allocationQuantity: 7);

        var released = await fixture.Service.ReleaseAsync(
            fixture.Allocation.Id, fixture.ActorId, 0, default);

        Assert.Equal((20m, 0m, 20m),
            (released.OnHandQuantity, released.ReservedQuantity, released.AvailableQuantity));
        Assert.Empty(fixture.Store.Transactions);
    }

    [Fact]
    public async Task Stale_expected_version_is_rejected()
    {
        var fixture = new Fixture(physicalQuantity: 20, allocationQuantity: 5);
        fixture.Balance.Version = 1;

        var exception = await Assert.ThrowsAsync<InventoryOperationException>(() =>
            fixture.Service.ReserveAsync(fixture.Allocation.Id, fixture.ActorId, 0, default));

        Assert.Equal("conflict", exception.Code);
        Assert.Contains("version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Fixture
    {
        public Guid ActorId { get; } = Guid.NewGuid();
        public InventoryAllocation Allocation { get; }
        public InventoryBalance Balance { get; }
        public FakeStore Store { get; }
        public InventoryLifecycleService Service { get; }

        public Fixture(decimal physicalQuantity, decimal allocationQuantity)
        {
            var warehouseId = Guid.NewGuid();
            var batch = new Batch
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouseId,
                ProductId = Guid.NewGuid(),
                BatchCode = "WH-TA-SKU001-30112026",
                Quantity = physicalQuantity,
                StockStatus = BatchStockStatuses.InStock
            };
            Allocation = new InventoryAllocation
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                WarehouseId = warehouseId,
                AllocatedQuantity = allocationQuantity,
                Status = AllocationInventoryStatuses.Tentative
            };
            Balance = new InventoryBalance
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                PhysicalQuantity = physicalQuantity,
                AvailableQuantity = physicalQuantity
            };
            Store = new FakeStore(Allocation, batch, Balance);
            Service = new InventoryLifecycleService(Store, new FixedTimeProvider(Now));
        }
    }

    private sealed class FakeStore(
        InventoryAllocation allocation,
        Batch batch,
        InventoryBalance balance) : IInventoryLifecycleStore
    {
        public List<Reservation> Reservations { get; } = [];
        public List<InventoryTransaction> Transactions { get; } = [];

        public Task<InventoryLifecycleView?> FindForUpdateAsync(
            Guid allocationId, CancellationToken cancellationToken) =>
            Task.FromResult<InventoryLifecycleView?>(allocation.Id == allocationId
                ? new(allocation, batch, balance,
                    Reservations.SingleOrDefault(item => item.AllocationId == allocationId))
                : null);

        public void AddReservation(Reservation reservation) => Reservations.Add(reservation);
        public void AddTransaction(InventoryTransaction transaction) => Transactions.Add(transaction);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
