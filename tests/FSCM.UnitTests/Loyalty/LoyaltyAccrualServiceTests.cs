using FSCM.Application.Features.Loyalty;
using FSCM.Application.Features.Orders;

namespace FSCM.UnitTests.Loyalty;

public sealed class LoyaltyAccrualServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Adjustment_uses_net_points_already_awarded_and_is_idempotent()
    {
        var orderId = Guid.NewGuid();
        var retailerId = Guid.NewGuid();
        var balanceId = Guid.NewGuid();
        var store = new FakeLoyaltyAccrualStore();
        store.Balances[balanceId] = 10;
        store.Views[orderId] = new LoyaltyAccrualView(
            orderId,
            retailerId,
            OrderStatuses.Completed,
            ApprovedFinalAmount: 1_000m,
            PaymentSettled: true,
            HasOpenComplaint: false,
            new LoyaltyProgramSnapshot(Guid.NewGuid(), SpendAmountPerPoint: 100m),
            new RetailerBalanceSnapshot(balanceId, CurrentPoints: 10, LifetimeEarnedPoints: 10),
            AlreadyEarned: true,
            HasLivePending: false,
            EarnedPointsForOrder: 10);
        var service = new LoyaltyAccrualService(store, new FixedTimeProvider(Now));

        var first = await service.AdjustAsync(
            orderId, 800m, "Khiếu nại được chấp nhận", Guid.NewGuid(), default);
        var replay = await service.AdjustAsync(
            orderId, 800m, "Khiếu nại được chấp nhận", Guid.NewGuid(), default);

        Assert.Equal(-2, first.Points);
        Assert.Equal(8, first.BalanceAfter);
        Assert.Equal(0, replay.Points);
        Assert.Single(store.Rows);
        Assert.Equal(LoyaltyTransactionTypes.EarnedAdjusted, store.Rows[0].Type);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
