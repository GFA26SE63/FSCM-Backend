using FSCM.Application.Features.Loyalty;
using FSCM.Domain.Entities;
using FSCM.Application.Features.Orders;

namespace FSCM.UnitTests.Loyalty;

public sealed class LoyaltyProgramSelectionTests
{
    [Fact]
    public void Chooses_most_whole_points_not_the_latest_program()
    {
        var older = new LoyaltyProgram { Id = Guid.NewGuid(), Code = "LP-002", SpendAmountPerPoint = 100 };
        var newer = new LoyaltyProgram { Id = Guid.NewGuid(), Code = "LP-001", SpendAmountPerPoint = 200 };
        Assert.Same(older, LoyaltyProgramSelection.BestForAmount([newer, older], 1_000));
    }

    [Fact]
    public void Equal_rounded_points_are_resolved_by_code_even_if_rates_differ()
    {
        var a = new LoyaltyProgram { Id = Guid.NewGuid(), Code = "LP-001", SpendAmountPerPoint = 70 };
        var b = new LoyaltyProgram { Id = Guid.NewGuid(), Code = "LP-002", SpendAmountPerPoint = 60 };
        Assert.Same(a, LoyaltyProgramSelection.BestForAmount([b, a], 100));
        Assert.Same(a, LoyaltyProgramSelection.BestForAmount([a, b], 100));
        Assert.Equal(0, LoyaltyProgramSelection.PointsFor(-1, 100));
        Assert.Null(LoyaltyProgramSelection.BestForAmount([], 100));
    }

    [Fact]
    public async Task Completion_cancels_original_pending_amount_and_awards_once_at_new_rate()
    {
        var order = Guid.NewGuid(); var balance = Guid.NewGuid();
        var store = new FakeLoyaltyAccrualStore();
        store.Views[order] = new(order, Guid.NewGuid(), OrderStatuses.Completed, 1_000, true, false,
            new(Guid.NewGuid(), 50), new(balance, 0, 0), false, true) { PendingPointsForOrder = 10 };
        var service = new LoyaltyAccrualService(store, TimeProvider.System);
        var first = await service.EvaluateAsync(order, null, default);
        await service.EvaluateAsync(order, null, default);
        Assert.Equal(20, first.Points);
        Assert.Single(store.Rows, r => r.Type == LoyaltyTransactionTypes.Earned);
        Assert.Equal(-10, Assert.Single(store.Rows, r => r.Type == LoyaltyTransactionTypes.PendingEarnCancelled).Points);
        Assert.Equal(20, store.Balances[balance]);
    }

    [Fact]
    public async Task Without_an_eligible_program_no_points_or_pending_row_are_created()
    {
        var order = Guid.NewGuid(); var store = new FakeLoyaltyAccrualStore();
        store.Views[order] = new(order, Guid.NewGuid(), OrderStatuses.Completed, 1_000, true, false, null, null, false, false);
        var result = await new LoyaltyAccrualService(store, TimeProvider.System).EvaluateAsync(order, null, default);
        Assert.False(result.Earned); Assert.Empty(store.Rows); Assert.Empty(store.Balances);
    }

    [Fact]
    public async Task Program_no_longer_eligible_clears_old_pending_without_spending_or_earning()
    {
        var order = Guid.NewGuid(); var balance = Guid.NewGuid(); var store = new FakeLoyaltyAccrualStore();
        store.Views[order] = new(order, Guid.NewGuid(), OrderStatuses.Completed, 1_000, true, false,
            null, new(balance, 500, 500), false, true)
        { PendingPointsForOrder = 10, PendingProgramId = Guid.NewGuid() };
        var result = await new LoyaltyAccrualService(store, TimeProvider.System).EvaluateAsync(order, null, default);
        Assert.False(result.Earned); Assert.Equal(500, result.BalanceAfter);
        var cancellation = Assert.Single(store.Rows);
        Assert.Equal(LoyaltyTransactionTypes.PendingEarnCancelled, cancellation.Type);
        Assert.Equal(-10, cancellation.Points); Assert.False(cancellation.MovesBalance);
    }
}
