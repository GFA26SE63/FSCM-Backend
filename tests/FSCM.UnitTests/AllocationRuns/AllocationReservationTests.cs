using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Inventory;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.AllocationRuns;

/// <summary>
/// F88: holding the stock an approved plan promised. Checked again against live stock, all or
/// nothing, idempotent, and OnHand never moves.
/// </summary>
public sealed class AllocationReservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 7, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid PlanVersion = Guid.NewGuid();

    [Fact]
    public async Task Holding_raises_reserved_and_lowers_available_but_never_touches_on_hand()
    {
        var fixture = new Fixture();
        fixture.AddAllocation(quantity: 12m, physical: 40m);

        var outcome = await fixture.Service.ReserveAsync(PlanVersion, Actor, "DOT-20261022-01");

        var balance = fixture.Store.Balances.Values.Single();
        Assert.Equal(1, outcome.ReservedCount);
        Assert.Equal(12m, outcome.ReservedQuantity);
        Assert.Equal(40m, balance.PhysicalQuantity);
        Assert.Equal(12m, balance.ReservedQuantity);
        Assert.Equal(28m, balance.AvailableQuantity);
    }

    [Fact]
    public async Task A_hold_is_written_to_the_ledger_and_the_transaction_log()
    {
        var fixture = new Fixture();
        fixture.AddAllocation(quantity: 5m, physical: 20m);

        await fixture.Service.ReserveAsync(PlanVersion, Actor, "DOT-20261022-01");

        var reservation = Assert.Single(fixture.Store.Reservations);
        Assert.Equal("Active", reservation.Status);
        Assert.NotNull(reservation.AllocationRunAllocationId);
        Assert.Null(reservation.AllocationId);

        var transaction = Assert.Single(fixture.Store.Transactions);
        Assert.Equal(0m, transaction.PhysicalQuantityDelta);
        Assert.Equal(5m, transaction.ReservedQuantityDelta);
        Assert.Equal("DOT-20261022-01", transaction.ReferenceCode);
    }

    [Fact]
    public async Task Reserving_twice_does_not_hold_the_same_stock_again()
    {
        var fixture = new Fixture();
        fixture.AddAllocation(quantity: 5m, physical: 20m);
        await fixture.Service.ReserveAsync(PlanVersion, Actor, "DOT-20261022-01");

        var second = await fixture.Service.ReserveAsync(PlanVersion, Actor, "DOT-20261022-01");

        Assert.Equal(0, second.ReservedCount);
        Assert.Equal(1, second.AlreadyHeldCount);
        Assert.Equal(5m, fixture.Store.Balances.Values.Single().ReservedQuantity);
        Assert.Single(fixture.Store.Reservations);
    }

    [Fact]
    public async Task Stock_drawn_down_after_the_plan_was_built_stops_the_whole_hold()
    {
        var fixture = new Fixture();
        // Two lines on different batches; the second no longer has enough.
        fixture.AddAllocation(quantity: 5m, physical: 20m);
        fixture.AddAllocation(quantity: 30m, physical: 10m);

        var exception = await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            fixture.Service.ReserveAsync(PlanVersion, Actor, "DOT-20261022-01", maxAttempts: 1));

        Assert.Equal("conflict", exception.Code);
        Assert.Contains("available", exception.Message);
        // Nothing is left half held: the transaction rolled back.
        Assert.Empty(fixture.Store.Reservations);
        Assert.All(fixture.Store.Balances.Values, balance => Assert.Equal(0m, balance.ReservedQuantity));
    }

    [Fact]
    public async Task A_batch_that_expired_after_the_plan_was_built_stops_the_hold()
    {
        var fixture = new Fixture();
        fixture.AddAllocation(quantity: 5m, physical: 20m, expiryStatus: BatchExpiryStatuses.Expired);

        var exception = await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            fixture.Service.ReserveAsync(PlanVersion, Actor, "DOT-20261022-01", maxAttempts: 1));

        Assert.Equal("conflict", exception.Code);
        Assert.Contains("expired", exception.Message);
        Assert.Empty(fixture.Store.Reservations);
    }

    [Fact]
    public async Task A_conflict_is_retried_up_to_the_configured_limit()
    {
        var fixture = new Fixture();
        fixture.AddAllocation(quantity: 30m, physical: 10m);

        var exception = await Assert.ThrowsAsync<AllocationRunOperationException>(() =>
            fixture.Service.ReserveAsync(PlanVersion, Actor, "DOT-20261022-01", maxAttempts: 3));

        Assert.Equal("conflict", exception.Code);
        Assert.Equal(3, fixture.Store.ReadCount);
    }

    [Fact]
    public async Task A_retry_succeeds_once_the_stock_is_back()
    {
        var fixture = new Fixture();
        fixture.AddAllocation(quantity: 30m, physical: 10m);
        // Whoever was holding it releases between the first read and the second.
        fixture.Store.OnRead = count =>
        {
            if (count == 1) fixture.Store.Balances.Values.Single().AvailableQuantity = 50m;
        };

        var outcome = await fixture.Service.ReserveAsync(PlanVersion, Actor, "DOT-20261022-01");

        Assert.Equal(1, outcome.ReservedCount);
        Assert.Equal(2, outcome.Attempts);
    }

    [Fact]
    public async Task A_plan_that_allocated_nothing_is_not_an_error()
    {
        var fixture = new Fixture();

        var outcome = await fixture.Service.ReserveAsync(PlanVersion, Actor, "DOT-20261022-01");

        Assert.Equal(0, outcome.ReservedCount);
        Assert.Empty(fixture.Store.Reservations);
    }

    private sealed class Fixture
    {
        public FakeStore Store { get; } = new();
        public AllocationReservationService Service { get; }

        public Fixture() => Service = new AllocationReservationService(Store, new FixedClock());

        public void AddAllocation(
            decimal quantity, decimal physical, string expiryStatus = BatchExpiryStatuses.Valid)
        {
            var batchId = Guid.NewGuid();
            Store.Batches[batchId] = new Batch
            {
                Id = batchId,
                BatchCode = $"B-{Store.Batches.Count + 1:000}",
                WarehouseId = Guid.NewGuid(),
                ProductId = Guid.NewGuid(),
                ExpiryStatus = expiryStatus
            };
            Store.Balances[batchId] = new InventoryBalance
            {
                BatchId = batchId,
                PhysicalQuantity = physical,
                ReservedQuantity = 0m,
                AvailableQuantity = physical
            };
            Store.Allocations.Add(new AllocationRunAllocation
            {
                Id = Guid.NewGuid(),
                AllocationPlanVersionId = PlanVersion,
                BatchId = batchId,
                WarehouseId = Store.Batches[batchId].WarehouseId,
                Quantity = quantity
            });
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeStore : StubAllocationRunStore
    {
        public List<AllocationRunAllocation> Allocations { get; } = [];
        public Dictionary<Guid, Batch> Batches { get; } = [];
        public Dictionary<Guid, InventoryBalance> Balances { get; } = [];
        public List<Reservation> Reservations { get; } = [];
        public List<InventoryTransaction> Transactions { get; } = [];
        public int ReadCount { get; private set; }
        public Action<int>? OnRead { get; set; }

        public override Task<IReadOnlyCollection<ReservableAllocation>> ListReservableAsync(
            Guid planVersionId, CancellationToken cancellationToken)
        {
            OnRead?.Invoke(ReadCount);
            ReadCount++;
            return Task.FromResult<IReadOnlyCollection<ReservableAllocation>>(Allocations
                .Where(item => item.AllocationPlanVersionId == planVersionId)
                .Select(item => new ReservableAllocation(
                    item,
                    Batches[item.BatchId],
                    Balances[item.BatchId],
                    Reservations.Any(reservation =>
                        reservation.AllocationRunAllocationId == item.Id && reservation.Status == "Active")))
                .ToArray());
        }

        public override void AddReservation(Reservation reservation) => Reservations.Add(reservation);

        public override void AddInventoryTransaction(InventoryTransaction transaction) =>
            Transactions.Add(transaction);

        /// <summary>A rollback puts back whatever the failed attempt had already written.</summary>
        public override async Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> action, CancellationToken cancellationToken)
        {
            var reservationCount = Reservations.Count;
            var transactionCount = Transactions.Count;
            var snapshot = Balances.ToDictionary(
                pair => pair.Key,
                pair => (pair.Value.PhysicalQuantity, pair.Value.ReservedQuantity, pair.Value.AvailableQuantity));
            try
            {
                await action(cancellationToken);
            }
            catch
            {
                Reservations.RemoveRange(reservationCount, Reservations.Count - reservationCount);
                Transactions.RemoveRange(transactionCount, Transactions.Count - transactionCount);
                foreach (var (batchId, values) in snapshot)
                {
                    var balance = Balances[batchId];
                    balance.PhysicalQuantity = values.PhysicalQuantity;
                    balance.ReservedQuantity = values.ReservedQuantity;
                    balance.AvailableQuantity = values.AvailableQuantity;
                }

                throw;
            }
        }
    }
}
