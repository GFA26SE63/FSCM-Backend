using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Allocation;

namespace FSCM.UnitTests.AllocationRuns;

public sealed class AllocationPlannerReport5Tests
{
    private static readonly DateTimeOffset Arrival = new(2026, 11, 18, 2, 0, 0, TimeSpan.Zero);
    private static readonly Guid Product = Guid.NewGuid();

    [Fact]
    public void UT_Allocation_Planner_UTCID02_equal_accepted_priority_is_tiebroken_by_immutable_so_code()
    {
        var stock = Batch("B-LAST-10", "WH-A", 10m);
        var plan = NewPlanner().Plan(
            // Deliberately reverse the input sequence: worker scheduling must not decide priority.
            [Order("SO-B", 1, 10m), Order("SO-A", 1, 10m)], [stock], Route);

        Assert.Collection(plan.Orders,
            first =>
            {
                Assert.Equal("SO-A", first.OrderCode);
                Assert.True(first.IsFullyAllocated);
                Assert.Equal(10m, Assert.Single(first.Allocations).Quantity);
            },
            second =>
            {
                Assert.Equal("SO-B", second.OrderCode);
                Assert.False(second.IsFullyAllocated);
                Assert.Empty(second.Allocations);
            });
    }

    [Fact]
    public void UT_Allocation_Planner_UTCID05_retains_batch_and_independent_warehouse_traces_for_a_split_order()
    {
        var warehouseA = Batch("B-REGION-A", "WH-A", 6m);
        var warehouseB = Batch("B-REGION-B", "WH-B", 4m);
        var plan = NewPlanner().Plan([Order("SO-SPLIT", 1, 10m)], [warehouseA, warehouseB], Route);

        var order = Assert.Single(plan.Orders);
        Assert.True(order.IsFullyAllocated);
        Assert.Equal(10m, order.Allocations.Sum(item => item.Quantity));
        Assert.Collection(order.Allocations.OrderBy(item => item.Sequence),
            first =>
            {
                Assert.Equal(warehouseA.BatchId, first.BatchId);
                Assert.Equal(warehouseA.WarehouseId, first.WarehouseId);
                Assert.Equal(6m, first.Quantity);
                Assert.Equal(Arrival, first.EstimatedArrivalAt);
            },
            second =>
            {
                Assert.Equal(warehouseB.BatchId, second.BatchId);
                Assert.Equal(warehouseB.WarehouseId, second.WarehouseId);
                Assert.Equal(4m, second.Quantity);
                Assert.Equal(Arrival, second.EstimatedArrivalAt);
            });
        Assert.Equal(0m, plan.RemainingPool[warehouseA.BatchId]);
        Assert.Equal(0m, plan.RemainingPool[warehouseB.BatchId]);
    }

    private static AllocationPlanner NewPlanner() => new(new AllocationEngine());

    private static PlannerOrder Order(string code, int priorityRank, decimal quantity) => new(
        Guid.NewGuid(), code, Guid.NewGuid(), priorityRank, DateOnly.FromDateTime(Arrival.UtcDateTime).AddDays(7),
        [new(Guid.NewGuid(), Product, "SKU-REPORT5", 1, quantity, 720, 0.25m)]);

    private static PlannerBatch Batch(string code, string warehouseCode, decimal quantity) => new(
        Guid.NewGuid(), code, Guid.NewGuid(), warehouseCode, Product, Arrival.AddHours(400), quantity);

    private static PlannerRoute Route(Guid warehouseId, Guid retailerId) =>
        new(5_000m, Arrival, DateOnly.FromDateTime(Arrival.UtcDateTime));
}
