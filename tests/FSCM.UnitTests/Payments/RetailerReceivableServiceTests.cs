using FSCM.Application.Features.Payments;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Payments;

public sealed class RetailerReceivableServiceTests
{
    private static readonly Guid RetailerUserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Uses_canonical_adjustment_dispute_and_refund_formulas()
    {
        var view = CreateView();
        var service = CreateService(view);

        var result = await service.GetByOrderAsync(RetailerUserId, view.Payment.Order.Id, default);

        Assert.Equal(1_100_000, result.OriginalOrderAmount);
        Assert.Equal(1_000_000, result.FinalApprovedAmount);
        Assert.Equal(100_000, result.ApprovedAdjustmentAmount);
        Assert.Equal(900_000, result.AdjustedAmount);
        Assert.Equal(700_000, result.ConfirmedCollectedAmount);
        Assert.Equal(200_000, result.OutstandingAmount);
        Assert.Equal(50_000, result.PendingDisputedAmount);
        Assert.Equal(150_000, result.CollectibleAmount);
        Assert.Equal(40_000, result.PendingRefundAmount);
        Assert.Equal(20_000, result.CompletedRefundAmount);
        Assert.Equal(0, result.RefundDueAmount);
        Assert.True(result.IsOverdue);
        Assert.Equal(5, result.DaysOverdue);
    }

    [Fact]
    public async Task Lists_with_filter_and_deterministic_pagination()
    {
        var first = CreateView("DH-000002");
        var second = CreateView("DH-000001", DateOnly.FromDateTime(Now.Date).AddDays(2));
        var service = CreateService(first, second);

        var page = await service.ListAsync(RetailerUserId,
            new(PaymentStatuses.Overdue, "000002", null, null, true, 1, 1), default);

        Assert.Single(page.Items);
        Assert.Equal(first.Payment.Order.Id, page.Items.Single().OrderId);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, page.TotalPages);
    }

    [Fact]
    public async Task Does_not_expose_another_retailers_order()
    {
        var view = CreateView();
        var service = new RetailerReceivableService(new FakeStore([], null),
            new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<PaymentOperationException>(() =>
            service.GetByOrderAsync(RetailerUserId, view.Payment.Order.Id, default));

        Assert.Equal("not_found", exception.Code);
    }

    private static RetailerReceivableService CreateService(params RetailerReceivableView[] views) =>
        new(new FakeStore(views, views.FirstOrDefault()), new FixedTimeProvider(Now));

    private static RetailerReceivableView CreateView(
        string orderCode = "DH-000002", DateOnly? dueDate = null)
    {
        var order = new SalesOrder
        {
            Id = Guid.NewGuid(),
            OrderCode = orderCode,
            RetailerId = Guid.NewGuid(),
            RetailerCodeSnapshot = "10004127",
            RetailerNameSnapshot = "Cửa hàng An Phú",
            SubtotalAmount = 1_100_000
        };
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            SalesOrderId = order.Id,
            Method = "Credit",
            Status = PaymentStatuses.Unpaid,
            Amount = 1_000_000,
            AdjustedAmount = 900_000,
            ConfirmedAmount = 700_000,
            DueDate = dueDate ?? DateOnly.FromDateTime(Now.Date).AddDays(-5),
            UpdatedAt = Now
        };
        var retailer = new Retailer { Id = order.RetailerId, UserAccountId = RetailerUserId };
        var paymentView = new PaymentView(payment, order, retailer, 40_000, 20_000, true);
        return new(paymentView,
            [new(Guid.NewGuid(), "BankTransfer", 700_000, PaymentStatuses.Paid, Now, "FT-001", null)],
            [new(Guid.NewGuid(), "Open", 50_000, Now.AddDays(-1), null, "Thiếu hàng")],
            [
                new(Guid.NewGuid(), 40_000, "AwaitingVerification", Now, "RF-001", null, "Chờ xác minh"),
                new(Guid.NewGuid(), 20_000, "Refunded", Now.AddDays(-1), "RF-000", Now, "Đã hoàn")
            ]);
    }

    private sealed class FakeStore(
        IReadOnlyCollection<RetailerReceivableView> views,
        RetailerReceivableView? detail) : IRetailerReceivableStore
    {
        public Task<IReadOnlyCollection<RetailerReceivableView>> ListAsync(
            Guid retailerUserId, CancellationToken cancellationToken) => Task.FromResult(views);

        public Task<RetailerReceivableView?> FindByOrderAsync(
            Guid retailerUserId, Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(detail?.Payment.Order.Id == orderId ? detail : null);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
