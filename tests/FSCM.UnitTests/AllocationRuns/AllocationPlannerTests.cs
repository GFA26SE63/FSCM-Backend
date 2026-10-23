using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Allocation;

namespace FSCM.UnitTests.AllocationRuns;

public sealed class AllocationPlannerTests
{
    private static readonly DateTimeOffset Arrival = new(2026, 10, 24, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly ArrivalDate = new(2026, 10, 24);
    private static readonly Guid ProductA = Guid.NewGuid();
    private static readonly Guid ProductB = Guid.NewGuid();

    [Fact]
    public void An_earlier_order_takes_stock_away_from_a_later_one()
    {
        var planner = NewPlanner();
        var batch = Batch("B-001", ProductA, quantity: 30m);

        var plan = planner.Plan(
            [Order("DH-000001", 1, Line(ProductA, "SKU-A", 25m)),
             Order("DH-000002", 2, Line(ProductA, "SKU-A", 25m))],
            [batch],
            Route);

        Assert.True(plan.Orders[0].IsFullyAllocated);
        Assert.False(plan.Orders[1].IsFullyAllocated);
        Assert.Equal(5m, plan.RemainingPool[batch.BatchId]);
    }

    [Fact]
    public void An_earlier_orders_allocation_is_never_taken_back_for_a_later_one()
    {
        var planner = NewPlanner();
        var only = Batch("B-001", ProductA, quantity: 10m);

        var plan = planner.Plan(
            [Order("DH-EARLY", 1, Line(ProductA, "SKU-A", 10m)),
             Order("DH-LATE", 2, Line(ProductA, "SKU-A", 10m))],
            [only],
            Route);

        // Scope V10 cut batch swapping: the later order goes short rather than the earlier one
        // being moved aside to make room.
        Assert.True(plan.Orders[0].IsFullyAllocated);
        Assert.Equal(only.BatchId, Assert.Single(plan.Orders[0].Allocations).BatchId);
        Assert.False(plan.Orders[1].IsFullyAllocated);
        Assert.Equal(0m, plan.RemainingPool[only.BatchId]);
    }

    [Fact]
    public void A_short_line_hands_back_every_draw_the_order_already_made()
    {
        var planner = NewPlanner();
        var plentiful = Batch("B-A", ProductA, quantity: 100m);
        var scarce = Batch("B-B", ProductB, quantity: 1m);

        var plan = planner.Plan(
            [Order("DH-000001", 1, Line(ProductA, "SKU-A", 10m), Line(ProductB, "SKU-B", 10m))],
            [plentiful, scarce],
            Route);

        var order = Assert.Single(plan.Orders);
        Assert.False(order.IsFullyAllocated);
        Assert.Empty(order.Allocations);
        // The first line had already drawn 10 from B-A; it must be back in the pool.
        Assert.Equal(100m, plan.RemainingPool[plentiful.BatchId]);
        Assert.Equal(1m, plan.RemainingPool[scarce.BatchId]);
    }

    [Fact]
    public void Stock_released_by_a_short_order_is_available_to_the_orders_behind_it()
    {
        var planner = NewPlanner();
        var shared = Batch("B-A", ProductA, quantity: 10m);
        var scarce = Batch("B-B", ProductB, quantity: 0m);

        var plan = planner.Plan(
            [
                // Needs both SKUs, but SKU-B has nothing, so it gives SKU-A back.
                Order("DH-000001", 1, Line(ProductA, "SKU-A", 10m), Line(ProductB, "SKU-B", 5m)),
                Order("DH-000002", 2, Line(ProductA, "SKU-A", 10m))
            ],
            [shared, scarce],
            Route);

        Assert.False(plan.Orders[0].IsFullyAllocated);
        Assert.True(plan.Orders[1].IsFullyAllocated);
        Assert.Equal(0m, plan.RemainingPool[shared.BatchId]);
    }

    [Fact]
    public void An_order_may_draw_one_line_from_several_batches_and_warehouses()
    {
        var planner = NewPlanner();
        var near = Batch("B-NEAR", ProductA, quantity: 6m, distanceMeters: 2_000m);
        var far = Batch("B-FAR", ProductA, quantity: 6m, distanceMeters: 15_000m);

        var plan = planner.Plan([Order("DH-000001", 1, Line(ProductA, "SKU-A", 10m))], [near, far], Route);

        var order = Assert.Single(plan.Orders);
        Assert.True(order.IsFullyAllocated);
        Assert.Equal(2, order.Allocations.Count);
        Assert.Equal(10m, order.Allocations.Sum(item => item.Quantity));
    }

    [Fact]
    public void A_shortage_is_explained_by_line_and_quantity()
    {
        var planner = NewPlanner();

        var plan = planner.Plan(
            [Order("DH-000001", 1, Line(ProductA, "SKU-A", 40m))],
            [Batch("B-A", ProductA, quantity: 15m)],
            Route);

        var reason = Assert.Single(plan.Orders).ShortageReason;
        Assert.Contains("SKU-A", reason);
        Assert.Contains("25", reason);
    }

    [Fact]
    public void Line_results_are_kept_for_short_orders_so_a_trace_can_explain_them()
    {
        var planner = NewPlanner();

        var plan = planner.Plan(
            [Order("DH-000001", 1, Line(ProductA, "SKU-A", 40m))],
            [Batch("B-A", ProductA, quantity: 15m)],
            Route);

        var result = Assert.Single(Assert.Single(plan.Orders).LineResults).Result;
        Assert.False(result.IsFullyAllocated);
        Assert.NotEmpty(result.FilterTrace);
    }

    [Fact]
    public void Priority_rank_decides_the_order_not_the_sequence_passed_in()
    {
        var planner = NewPlanner();
        var batch = Batch("B-001", ProductA, quantity: 10m);

        var plan = planner.Plan(
            [Order("DH-LATE", 2, Line(ProductA, "SKU-A", 10m)),
             Order("DH-EARLY", 1, Line(ProductA, "SKU-A", 10m))],
            [batch],
            Route);

        Assert.Equal("DH-EARLY", plan.Orders[0].OrderCode);
        Assert.True(plan.Orders[0].IsFullyAllocated);
        Assert.False(plan.Orders[1].IsFullyAllocated);
    }

    private static AllocationPlanner NewPlanner() => new(new AllocationEngine());

    private static PlannerOrder Order(string code, int rank, params PlannerLine[] lines) =>
        new(Guid.NewGuid(), code, Guid.NewGuid(), rank, ArrivalDate.AddDays(7), lines);

    private static PlannerLine Line(Guid productId, string sku, decimal quantity) =>
        new(Guid.NewGuid(), productId, sku, 1, quantity, 720, 0.25m);

    private static PlannerBatch Batch(
        string code, Guid productId, decimal quantity, decimal distanceMeters = 5_000m)
    {
        var batch = new PlannerBatch(Guid.NewGuid(), code, Guid.NewGuid(), "WH-01", productId,
            Arrival.AddHours(400), quantity);
        Distances[batch.WarehouseId] = distanceMeters;
        return batch;
    }

    private static readonly Dictionary<Guid, decimal> Distances = [];

    /// <summary>Every warehouse reaches every retailer on the same day; only distance differs.</summary>
    private static PlannerRoute? Route(Guid warehouseId, Guid retailerId) =>
        new(Distances.GetValueOrDefault(warehouseId, 5_000m), Arrival, ArrivalDate);
}
