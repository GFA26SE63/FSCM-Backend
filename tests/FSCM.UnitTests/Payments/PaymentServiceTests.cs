using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Payments;

public sealed class PaymentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Record_cod_creates_pending_payment()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RecordAsync(fixture.OperatorId, SystemRoles.Operator,
            new RecordPaymentData(fixture.Order.Id, "COD", 100_000m, MarkPaid: false, "Thu khi giao"), default);

        Assert.Equal(PaymentMethods.CashOnDelivery, result.Method);
        Assert.Equal(PaymentStatuses.Pending, result.Status);
        Assert.Null(result.ConfirmedAt);
        Assert.Single(fixture.Store.Payments);
    }

    [Fact]
    public async Task Record_prepaid_marked_paid_sets_confirmation()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RecordAsync(fixture.OperatorId, SystemRoles.Operator,
            new RecordPaymentData(fixture.Order.Id, "PrepaidTransfer", 100_000m, MarkPaid: true, null), default);

        Assert.Equal(PaymentMethods.PrepaidTransfer, result.Method);
        Assert.Equal(PaymentStatuses.Paid, result.Status);
        Assert.Equal(fixture.OperatorId, result.ConfirmedByUserId);
        Assert.NotNull(result.ConfirmedAt);
    }

    [Fact]
    public async Task Confirm_moves_pending_to_paid()
    {
        var fixture = new Fixture();
        var created = await fixture.Service.RecordAsync(fixture.OperatorId, SystemRoles.Operator,
            new RecordPaymentData(fixture.Order.Id, "COD", 100_000m, MarkPaid: false, null), default);

        var confirmed = await fixture.Service.ConfirmAsync(created.Id, fixture.OperatorId, SystemRoles.Operator, "Da nhan", default);

        Assert.Equal(PaymentStatuses.Paid, confirmed.Status);
        Assert.NotNull(confirmed.ConfirmedAt);
    }

    [Fact]
    public async Task Record_rejects_overcollection_against_order_total()
    {
        var fixture = new Fixture();
        await fixture.Service.RecordAsync(fixture.OperatorId, SystemRoles.Operator,
            new RecordPaymentData(fixture.Order.Id, "PrepaidTransfer", 100_000m, MarkPaid: true, null), default);

        var exception = await Assert.ThrowsAsync<PaymentOperationException>(() =>
            fixture.Service.RecordAsync(fixture.OperatorId, SystemRoles.Operator,
                new RecordPaymentData(fixture.Order.Id, "COD", 1m, MarkPaid: true, null), default));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Record_rejects_invalid_method()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<PaymentOperationException>(() =>
            fixture.Service.RecordAsync(fixture.OperatorId, SystemRoles.Operator,
                new RecordPaymentData(fixture.Order.Id, "Bitcoin", 10m, MarkPaid: false, null), default));

        Assert.Equal("validation_error", exception.Code);
    }

    [Fact]
    public async Task Record_forbidden_for_unrelated_sales_user()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<PaymentOperationException>(() =>
            fixture.Service.RecordAsync(Guid.NewGuid(), SystemRoles.Sales,
                new RecordPaymentData(fixture.Order.Id, "COD", 10m, MarkPaid: false, null), default));

        Assert.Equal("forbidden", exception.Code);
    }

    private sealed class Fixture
    {
        public Guid OperatorId { get; } = Guid.NewGuid();
        public SalesOrder Order { get; }
        public FakeStore Store { get; }
        public PaymentService Service { get; }

        public Fixture()
        {
            Order = new SalesOrder
            {
                Id = Guid.NewGuid(),
                OrderCode = "SO-261013-01",
                CreatedByUserId = Guid.NewGuid(),
                SalesGroupId = Guid.NewGuid(),
                FinalAmount = 100_000m
            };
            Store = new FakeStore(Order);
            Service = new PaymentService(Store, new FixedTimeProvider(Now));
        }
    }

    private sealed class FakeStore(SalesOrder order) : IPaymentStore
    {
        public List<Payment> Payments { get; } = [];

        public Task<SalesOrder?> FindOrderAsync(Guid salesOrderId, CancellationToken cancellationToken) =>
            Task.FromResult(salesOrderId == order.Id ? order : null);

        public Task<Payment?> FindPaymentForUpdateAsync(Guid paymentId, CancellationToken cancellationToken) =>
            Task.FromResult(Payments.SingleOrDefault(item => item.Id == paymentId));

        public Task<PaymentView?> FindPaymentAsync(Guid paymentId, CancellationToken cancellationToken)
        {
            var payment = Payments.SingleOrDefault(item => item.Id == paymentId);
            return Task.FromResult(payment is null ? null : new PaymentView(payment, order.OrderCode));
        }

        public Task<IReadOnlyCollection<PaymentView>> ListByOrderAsync(Guid salesOrderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PaymentView>>(
                Payments.Where(item => item.SalesOrderId == salesOrderId)
                    .Select(item => new PaymentView(item, order.OrderCode)).ToArray());

        public Task<decimal> SumConfirmedAmountAsync(Guid salesOrderId, CancellationToken cancellationToken) =>
            Task.FromResult(Payments
                .Where(item => item.SalesOrderId == salesOrderId && item.Status == PaymentStatuses.Paid)
                .Sum(item => item.Amount));

        public Task<bool> CanAccessOrderAsync(Guid userId, string role, SalesOrder target, CancellationToken cancellationToken) =>
            Task.FromResult(role is SystemRoles.Admin or SystemRoles.Operator ||
                role == SystemRoles.Sales && target.CreatedByUserId == userId);

        public void AddPayment(Payment payment) => Payments.Add(payment);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
