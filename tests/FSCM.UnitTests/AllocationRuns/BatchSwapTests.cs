using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Allocation;

namespace FSCM.UnitTests.AllocationRuns;

/// <summary>
/// One-level batch swapping (F86). The registered example: order A holds 40 of batch B and a later
/// order C needs B. A may move to batch D only if D has at least 40 units nobody has claimed.
///
/// Deadlines are what make a batch unusable for one order and fine for another here: a batch that
/// arrives on day 5 suits an order due on day 30 and is useless to one due on day 2.
/// </summary>
public sealed class BatchSwapTests
{
    private static readonly DateTimeOffset Arrival = new(2026, 10, 24, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day0 = new(2026, 10, 24);
    private static readonly Guid Product = Guid.NewGuid();

    [Fact]
    public void An_earlier_order_moves_aside_so_a_later_one_can_be_served()
    {
        var planner = NewPlanner();
        var batchB = Batch("B", quantity: 40m, remainingLifeHours: 200, arrivesOnDay: 0);
        var batchD = Batch("D", quantity: 40m, remainingLifeHours: 500, arrivesOnDay: 5);

        var plan = planner.Plan(
            [
                // A can use either and prefers B: at equal distance the shorter life scores higher.
                Order("DH-A", 1, deadlineDay: 30, Line(40m, mlor: 0.20m)),
                // C is due on day 2, so D arrives too late and only B will do.
                Order("DH-C", 2, deadlineDay: 2, Line(40m, mlor: 0.20m))
            ],
            [batchB, batchD],
            Route);

        Assert.True(plan.Orders[0].IsFullyAllocated);
        Assert.True(plan.Orders[1].IsFullyAllocated);
        // A ends up on D and reports it, rather than still naming the batch it first chose.
        var movedTo = Assert.Single(plan.Orders[0].Allocations);
        Assert.Equal(batchD.BatchId, movedTo.BatchId);
        Assert.Equal(batchB.BatchId, movedTo.SwappedFromBatchId);
        Assert.Equal(batchB.BatchId, Assert.Single(plan.Orders[1].Allocations).BatchId);
    }

    [Fact]
    public void A_swap_restates_remaining_life_for_the_batch_actually_used()
    {
        var planner = NewPlanner();
        var batchB = Batch("B", quantity: 40m, remainingLifeHours: 200, arrivesOnDay: 0);
        var batchD = Batch("D", quantity: 40m, remainingLifeHours: 500, arrivesOnDay: 5);

        var plan = planner.Plan(
            [Order("DH-A", 1, 30, Line(40m, 0.20m)), Order("DH-C", 2, 2, Line(40m, 0.20m))],
            [batchB, batchD],
            Route);

        var moved = Assert.Single(plan.Orders[0].Allocations);
        Assert.Equal(500m, moved.RemainingShelfLifeHours);
    }

    [Fact]
    public void A_partly_assigned_alternative_offers_only_its_unclaimed_remainder()
    {
        var planner = NewPlanner();
        var batchB = Batch("B", quantity: 40m, remainingLifeHours: 200, arrivesOnDay: 0);
        // D holds 60, but a third order sits on 30 of it, leaving 30 free: too few for A's 40.
        var batchD = Batch("D", quantity: 60m, remainingLifeHours: 500, arrivesOnDay: 5);

        var plan = planner.Plan(
            [
                // E insists on 60% of 720 h, so B is too short-dated for it and it lands on D.
                Order("DH-E", 1, deadlineDay: 30, Line(30m, mlor: 0.60m)),
                Order("DH-A", 2, deadlineDay: 30, Line(40m, mlor: 0.20m)),
                Order("DH-C", 3, deadlineDay: 2, Line(40m, mlor: 0.20m))
            ],
            [batchB, batchD],
            Route);

        var thirdOrder = plan.Orders.Single(item => item.OrderCode == "DH-E");
        var shortOrder = plan.Orders.Single(item => item.OrderCode == "DH-C");
        Assert.Equal(batchD.BatchId, Assert.Single(thirdOrder.Allocations).BatchId);
        Assert.False(shortOrder.IsFullyAllocated);
        Assert.Contains(shortOrder.SwapAttempts, item => !item.Succeeded && item.Reason.Contains("chưa cấp"));
        // The third order keeps exactly what it had: a swap never takes from it.
        Assert.Equal(30m, thirdOrder.Allocations.Sum(item => item.Quantity));
    }

    [Fact]
    public void A_move_that_would_break_the_earlier_orders_own_mlor_is_refused()
    {
        var planner = NewPlanner();
        var batchB = Batch("B", quantity: 40m, remainingLifeHours: 400, arrivesOnDay: 0);
        // D is fresh enough for a lenient order but not for A, which insists on 50% of 720 h.
        var batchD = Batch("D", quantity: 40m, remainingLifeHours: 300, arrivesOnDay: 5);

        var plan = planner.Plan(
            [Order("DH-A", 1, deadlineDay: 30, Line(40m, mlor: 0.50m)),
             Order("DH-C", 2, deadlineDay: 2, Line(40m, mlor: 0.20m))],
            [batchB, batchD],
            Route);

        Assert.True(plan.Orders[0].IsFullyAllocated);
        Assert.False(plan.Orders[1].IsFullyAllocated);
        Assert.Contains(plan.Orders[1].SwapAttempts, item =>
            !item.Succeeded && item.Reason.Contains("BelowMlor"));
    }

    [Fact]
    public void Evaluations_stop_at_the_configured_limit_and_say_so()
    {
        var planner = NewPlanner();
        var batchB = Batch("B", quantity: 40m, remainingLifeHours: 200, arrivesOnDay: 0);
        // Several alternatives, each too small to take A's 40: every one burns an attempt.
        var alternatives = Enumerable.Range(1, 6)
            .Select(index => Batch($"D{index}", quantity: 5m, remainingLifeHours: 500, arrivesOnDay: 5))
            .ToArray();

        var plan = planner.Plan(
            [Order("DH-A", 1, deadlineDay: 30, Line(40m, mlor: 0.20m)),
             Order("DH-C", 2, deadlineDay: 2, Line(40m, mlor: 0.20m))],
            [batchB, .. alternatives],
            Route,
            swapAttemptLimit: 3);

        var shortOrder = plan.Orders[1];
        Assert.False(shortOrder.IsFullyAllocated);
        Assert.Equal(SwapStopReasons.AttemptLimitReached, shortOrder.SwapStopReason);
        Assert.Equal(3, shortOrder.SwapAttempts.Count);
    }

    [Fact]
    public void Failed_evaluations_are_recorded_not_just_the_successful_move()
    {
        var planner = NewPlanner();
        var batchB = Batch("B", quantity: 40m, remainingLifeHours: 200, arrivesOnDay: 0);
        var tooSmall = Batch("D1", quantity: 10m, remainingLifeHours: 500, arrivesOnDay: 5);
        var bigEnough = Batch("D2", quantity: 40m, remainingLifeHours: 600, arrivesOnDay: 5);

        var plan = planner.Plan(
            [Order("DH-A", 1, deadlineDay: 30, Line(40m, mlor: 0.20m)),
             Order("DH-C", 2, deadlineDay: 2, Line(40m, mlor: 0.20m))],
            [batchB, tooSmall, bigEnough],
            Route);

        var attempts = plan.Orders[1].SwapAttempts;
        Assert.Contains(attempts, item => !item.Succeeded && item.AlternativeBatchCode == "D1");
        Assert.Contains(attempts, item => item.Succeeded && item.AlternativeBatchCode == "D2");
        Assert.Equal(SwapStopReasons.LineFilled, plan.Orders[1].SwapStopReason);
    }

    [Fact]
    public void Nothing_moves_when_the_short_line_could_not_use_the_held_batch_anyway()
    {
        var planner = NewPlanner();
        // B is held by A but arrives after C's deadline, so freeing it would gain C nothing.
        var batchB = Batch("B", quantity: 40m, remainingLifeHours: 200, arrivesOnDay: 5);
        var batchD = Batch("D", quantity: 40m, remainingLifeHours: 900, arrivesOnDay: 5);

        var plan = planner.Plan(
            [Order("DH-A", 1, deadlineDay: 30, Line(40m, mlor: 0.20m)),
             Order("DH-C", 2, deadlineDay: 2, Line(40m, mlor: 0.20m))],
            [batchB, batchD],
            Route);

        Assert.False(plan.Orders[1].IsFullyAllocated);
        Assert.Contains(plan.Orders[1].SwapAttempts, item =>
            !item.Succeeded && item.Reason.Contains("không dùng được"));
        // A keeps the batch it chose.
        Assert.Equal(batchB.BatchId, Assert.Single(plan.Orders[0].Allocations).BatchId);
    }

    private static AllocationPlanner NewPlanner()
    {
        var engine = new AllocationEngine();
        return new AllocationPlanner(engine, new BatchSwapResolver(engine));
    }

    private static PlannerOrder Order(string code, int rank, int deadlineDay, params PlannerLine[] lines) =>
        new(Guid.NewGuid(), code, Guid.NewGuid(), rank, Day0.AddDays(deadlineDay), lines);

    private static PlannerLine Line(decimal quantity, decimal mlor) =>
        new(Guid.NewGuid(), Product, "SKU-A", 1, quantity, 720, mlor);

    private static readonly Dictionary<Guid, int> ArrivalDayByWarehouse = [];

    private static PlannerBatch Batch(
        string code, decimal quantity, int remainingLifeHours, int arrivesOnDay)
    {
        var arrivesAt = Arrival.AddDays(arrivesOnDay);
        var batch = new PlannerBatch(Guid.NewGuid(), code, Guid.NewGuid(), "WH-01", Product,
            arrivesAt.AddHours(remainingLifeHours), quantity);
        ArrivalDayByWarehouse[batch.WarehouseId] = arrivesOnDay;
        return batch;
    }

    /// <summary>The leg length is what makes a batch reachable for one order and not another.</summary>
    private static PlannerRoute? Route(Guid warehouseId, Guid retailerId)
    {
        var day = ArrivalDayByWarehouse.GetValueOrDefault(warehouseId);
        return new PlannerRoute(5_000m, Arrival.AddDays(day), Day0.AddDays(day));
    }
}
