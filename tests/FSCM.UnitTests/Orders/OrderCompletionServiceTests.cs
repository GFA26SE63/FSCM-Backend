using FSCM.Application.Features.Loyalty;
using FSCM.Application.Features.Orders;
using FSCM.UnitTests.Loyalty;

namespace FSCM.UnitTests.Orders;

public sealed class OrderCompletionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RetailerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProgramId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static (OrderCompletionService Service, FakeOrderCompletionStore Orders,
        FakeLoyaltyAccrualStore Points) Build(
        string status = OrderStatuses.Delivered, bool paid = true, bool complaint = false)
    {
        var orders = new FakeOrderCompletionStore();
        orders.Views[OrderId] = new OrderCompletionView(OrderId, status, paid, complaint);

        var points = new FakeLoyaltyAccrualStore();
        // The ledger sees the same order, and follows whatever the completion store does to it.
        points.Views[OrderId] = new LoyaltyAccrualView(
            OrderId, RetailerId, status, 1_000_000m, paid, complaint,
            new LoyaltyProgramSnapshot(ProgramId, 100_000m),
            new RetailerBalanceSnapshot(RetailerId, 0, 0), false, false);

        orders.OnCompleted = id => points.Views[id] = points.Views[id] with
        {
            OrderStatus = OrderStatuses.Completed
        };

        var clock = new FixedClock();
        return (new OrderCompletionService(orders, new LoyaltyAccrualService(points, clock), clock),
            orders, points);
    }

    [Fact]
    public async Task An_order_received_paid_and_undisputed_is_completed()
    {
        var (service, orders, _) = Build();

        var result = await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(OrderId, Assert.Single(orders.Completed));
    }

    [Fact]
    public async Task Completing_is_what_pays_the_points()
    {
        var (service, _, points) = Build();

        await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        // The status is committed first, so the ledger reads a settled order rather than being
        // told one is coming.
        Assert.Contains(points.Rows, row => row.Type == LoyaltyTransactionTypes.Earned);
        Assert.Equal(10, points.Balances[RetailerId]);
    }

    [Theory]
    [InlineData(OrderStatuses.Delivering, true, false, OrderCompletionBlock.NotDelivered)]
    [InlineData(OrderStatuses.Delivered, false, false, OrderCompletionBlock.NotFullyPaid)]
    [InlineData(OrderStatuses.Delivered, true, true, OrderCompletionBlock.ComplaintOpen)]
    public async Task Any_one_condition_missing_keeps_the_order_open(
        string status, bool paid, bool complaint, OrderCompletionBlock expected)
    {
        var (service, orders, points) = Build(status, paid, complaint);

        var result = await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Equal(expected, result.Block);
        Assert.Empty(orders.Completed);
        // And no points: paying on an order that is not settled is the whole thing to avoid.
        Assert.DoesNotContain(points.Rows, row => row.Type == LoyaltyTransactionTypes.Earned);
    }

    [Fact]
    public async Task An_order_that_is_not_settled_still_gets_a_reason_on_the_card()
    {
        var (service, _, points) = Build(paid: false);

        await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        // "Chờ cộng" with a reason, not a blank: the retailer is looking at their own points.
        var hold = Assert.Single(points.Rows, row => row.Type == LoyaltyTransactionTypes.PendingEarn);
        Assert.Contains("thanh toán", hold.Note);
        Assert.Equal(10, hold.Points);
    }

    [Fact]
    public async Task Completing_twice_pays_once()
    {
        var (service, orders, points) = Build();
        await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        // The order is now Completed; a later receipt or payment asks again.
        points.Views[OrderId] = points.Views[OrderId] with
        {
            OrderStatus = OrderStatuses.Completed, AlreadyEarned = true
        };
        var again = await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        Assert.False(again.Completed);
        Assert.Equal(OrderCompletionBlock.AlreadyCompleted, again.Block);
        Assert.Single(orders.Completed);
        Assert.Single(points.Rows, row => row.Type == LoyaltyTransactionTypes.Earned);
        Assert.Equal(10, points.Balances[RetailerId]);
    }

    [Fact]
    public async Task An_unknown_order_is_not_an_error()
    {
        var service = new OrderCompletionService(
            new FakeOrderCompletionStore(),
            new LoyaltyAccrualService(new FakeLoyaltyAccrualStore(), new FixedClock()),
            new FixedClock());

        var result = await service.EvaluateAsync(OrderId, null, CancellationToken.None);

        // Callers evaluate after every receipt and payment; one that cannot be resolved must not
        // take a delivery or a payment down with it.
        Assert.Equal(OrderCompletionBlock.NotFound, result.Block);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
