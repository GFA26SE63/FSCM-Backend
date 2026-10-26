using FSCM.Application.Features.Loyalty;
using FSCM.Application.Features.Orders;

namespace FSCM.UnitTests.Loyalty;

public sealed class LoyaltyAccrualServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RetailerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProgramId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>An order that meets every condition, unless a caller says otherwise.</summary>
    private static LoyaltyAccrualView Qualifying(
        string status = OrderStatuses.Delivered,
        decimal amount = 1_000_000m,
        bool paid = true,
        bool complaint = false,
        bool earned = false,
        bool pending = false,
        bool program = true) =>
        new(OrderId, RetailerId, status, amount, paid, complaint,
            program ? new LoyaltyProgramSnapshot(ProgramId, 100_000m) : null,
            new RetailerBalanceSnapshot(RetailerId, 0, 0), earned, pending);

    private static (LoyaltyAccrualService Service, FakeLoyaltyAccrualStore Store) Build(
        LoyaltyAccrualView? view)
    {
        var store = new FakeLoyaltyAccrualStore();
        if (view is not null) store.Views[OrderId] = view;
        return (new LoyaltyAccrualService(store, new FixedClock()), store);
    }

    [Fact]
    public async Task An_order_that_has_arrived_and_been_paid_for_earns_its_points()
    {
        var (service, store) = Build(Qualifying());

        var result = await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        // 1.000.000 at 100.000 per point.
        Assert.True(result.Earned);
        Assert.Equal(10, result.Points);
        Assert.Equal(10, store.Balances[RetailerId]);
        Assert.Equal(LoyaltyTransactionTypes.Earned, Assert.Single(store.Rows).Type);
    }

    [Fact]
    public async Task Points_come_from_the_approved_total_not_from_a_round_number()
    {
        // 1.049.999 at 100.000 per point is ten points and a bit; the bit is not a point.
        var (service, _) = Build(Qualifying(amount: 1_049_999m));

        var result = await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        Assert.Equal(10, result.Points);
    }

    [Fact]
    public async Task Evaluating_twice_does_not_pay_twice()
    {
        var (service, store) = Build(Qualifying());
        await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        // The store now reports the order as earned, which is what a second read would find.
        store.Views[OrderId] = Qualifying(earned: true);
        var again = await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        Assert.False(again.Earned);
        Assert.Equal(10, store.Balances[RetailerId]);
        Assert.Single(store.Rows, row => row.Type == LoyaltyTransactionTypes.Earned);
    }

    [Theory]
    [InlineData(OrderStatuses.Delivering, LoyaltyAccrualBlock.NotFullyReceived)]
    [InlineData(OrderStatuses.Issued, LoyaltyAccrualBlock.NotFullyReceived)]
    public async Task An_order_still_on_its_way_waits(string status, LoyaltyAccrualBlock expected)
    {
        var (service, store) = Build(Qualifying(status: status));

        var result = await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        Assert.False(result.Earned);
        Assert.Equal(expected, result.Block);
        // Held, not silently dropped: the card shows what the order will be worth.
        Assert.Equal(LoyaltyTransactionTypes.PendingEarn, Assert.Single(store.Rows).Type);
        Assert.Equal(10, store.Rows[0].Points);
        Assert.False(store.Rows[0].MovesBalance);
        Assert.Equal(0, store.Balances.GetValueOrDefault(RetailerId));
    }

    [Fact]
    public async Task An_unpaid_order_waits_even_though_it_has_arrived()
    {
        var (service, _) = Build(Qualifying(paid: false));

        var result = await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        Assert.Equal(LoyaltyAccrualBlock.NotFullyPaid, result.Block);
    }

    [Fact]
    public async Task An_order_under_complaint_waits_until_the_complaint_is_settled()
    {
        var (service, _) = Build(Qualifying(complaint: true));

        var result = await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        // Paying out on an order whose quantity is in dispute is a refund waiting to happen.
        Assert.Equal(LoyaltyAccrualBlock.ComplaintOpen, result.Block);
    }

    [Fact]
    public async Task A_hold_is_written_once_however_often_the_order_is_asked_about()
    {
        var (service, store) = Build(Qualifying(paid: false));
        await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        // A credit order sits unpaid for weeks; every receipt and payment touch asks again.
        store.Views[OrderId] = Qualifying(paid: false, pending: true);
        await service.EvaluateAsync(OrderId, null, CancellationToken.None);
        await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        Assert.Single(store.Rows, row => row.Type == LoyaltyTransactionTypes.PendingEarn);
    }

    [Fact]
    public async Task Earning_cancels_the_hold_rather_than_erasing_it()
    {
        var (service, store) = Build(Qualifying(pending: true));

        await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        // Both rows stay on the ledger: a row that disappears takes its explanation with it.
        Assert.Contains(store.Rows, row => row.Type == LoyaltyTransactionTypes.PendingEarnCancelled);
        Assert.Contains(store.Rows, row => row.Type == LoyaltyTransactionTypes.Earned);
        Assert.Equal(10, store.Balances[RetailerId]);
    }

    [Fact]
    public async Task With_no_programme_running_nothing_is_earned_and_nothing_is_promised()
    {
        var (service, store) = Build(Qualifying(program: false));

        var result = await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        Assert.Equal(LoyaltyAccrualBlock.NoActiveProgram, result.Block);
        // Nothing is held either: there is no rate to hold a number at.
        Assert.Empty(store.Rows);
    }

    [Fact]
    public async Task An_order_too_small_to_earn_a_point_earns_nothing()
    {
        var (service, store) = Build(Qualifying(amount: 99_999m));

        var result = await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        Assert.Equal(LoyaltyAccrualBlock.NothingToEarn, result.Block);
        Assert.Empty(store.Rows);
    }

    [Fact]
    public async Task An_unknown_order_is_not_an_error()
    {
        var (service, store) = Build(null);

        var result = await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        // Callers evaluate after every receipt and payment; one that cannot be resolved must not
        // take a delivery or a payment down with it.
        Assert.False(result.Earned);
        Assert.Empty(store.Rows);
    }

    [Fact]
    public async Task A_refund_takes_back_the_difference_and_may_go_negative()
    {
        var (service, store) = Build(Qualifying(earned: true));
        store.Balances[RetailerId] = 2;

        // The order was worth 1.000.000 and earned 10; it is now worth 300.000, so 3.
        var result = await service.AdjustAsync(
            OrderId, 300_000m, "Hoàn tiền sau khiếu nại", null, CancellationToken.None);

        Assert.Equal(-7, result.Points);
        // Points already spent cannot be un-spent, so the balance owes. Redemption refuses while
        // it does, which is what stops the difference being handed out twice.
        Assert.Equal(-5, store.Balances[RetailerId]);
        Assert.Equal(LoyaltyTransactionTypes.EarnedAdjusted, Assert.Single(store.Rows).Type);
    }

    [Fact]
    public async Task An_adjustment_that_changes_nothing_writes_nothing()
    {
        var (service, store) = Build(Qualifying(earned: true));

        var result = await service.AdjustAsync(
            OrderId, 1_000_000m, "Đối soát lại, không đổi", null, CancellationToken.None);

        Assert.Equal(0, result.Points);
        Assert.Empty(store.Rows);
    }

    [Fact]
    public async Task An_adjustment_needs_a_reason()
    {
        var (service, _) = Build(Qualifying(earned: true));

        await Assert.ThrowsAsync<LoyaltyOperationException>(() =>
            service.AdjustAsync(OrderId, 300_000m, "   ", null, CancellationToken.None));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
