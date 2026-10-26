using FSCM.Application.Features.ManualPayments;
using FSCM.Application.Features.Loyalty;
using FSCM.UnitTests.Loyalty;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.ManualPayments;

public sealed class ManualPaymentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Record_cod_creates_pending_payment_after_delivery()
    {
        var fixture = new Fixture();

        var result = await fixture.RecordAsync("manual-cod-001", markPaid: false);

        Assert.Equal(ManualPaymentMethods.CashOnDelivery, result.Method);
        Assert.Equal(ManualPaymentStatuses.Pending, result.Status);
        Assert.Equal(fixture.Ledger.Amount, result.OutstandingAmount);
        Assert.Null(result.ConfirmedAt);
    }

    [Fact]
    public async Task Prepaid_manual_confirmation_updates_canonical_ledger()
    {
        var fixture = new Fixture(OrderPaymentMethods.Prepaid, delivered: false);

        var result = await fixture.RecordAsync(
            "manual-prepaid-001", markPaid: true, method: ManualPaymentMethods.PrepaidTransfer);

        Assert.Equal(ManualPaymentStatuses.Paid, result.Status);
        Assert.Equal(fixture.Ledger.Amount, fixture.Ledger.ConfirmedAmount);
        Assert.Equal(PaymentStatuses.Paid, fixture.Ledger.Status);
        Assert.Equal(0, result.OutstandingAmount);
    }

    [Fact]
    public async Task Cod_is_rejected_before_complete_delivery()
    {
        var fixture = new Fixture(delivered: false);

        var exception = await Assert.ThrowsAsync<ManualPaymentOperationException>(() =>
            fixture.RecordAsync("manual-cod-early", markPaid: false));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Full_only_rule_rejects_partial_collection()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<ManualPaymentOperationException>(() =>
            fixture.Service.RecordAsync(fixture.OperatorId, SystemRoles.Operator,
                new(fixture.Order.Id, ManualPaymentMethods.CashOnDelivery, 50_000m, false, null),
                "manual-partial-001", default));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Record_is_idempotent_and_rejects_changed_payload()
    {
        var fixture = new Fixture();
        var first = await fixture.RecordAsync("manual-idempotent-001", markPaid: false);
        var replay = await fixture.RecordAsync("manual-idempotent-001", markPaid: false);

        Assert.Equal(first.Id, replay.Id);
        var exception = await Assert.ThrowsAsync<ManualPaymentOperationException>(() =>
            fixture.Service.RecordAsync(fixture.OperatorId, SystemRoles.Operator,
                new(fixture.Order.Id, ManualPaymentMethods.CashOnDelivery,
                    fixture.Ledger.Amount, false, "Nội dung khác"),
                "manual-idempotent-001", default));
        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Confirm_pending_payment_updates_both_records_once()
    {
        var fixture = new Fixture();
        var pending = await fixture.RecordAsync("manual-confirm-001", markPaid: false);

        var confirmed = await fixture.Service.ConfirmAsync(
            pending.Id, fixture.OperatorId, SystemRoles.Operator, new(0, "Đã nhận đủ tiền"), default);
        var replay = await fixture.Service.ConfirmAsync(
            pending.Id, fixture.OperatorId, SystemRoles.Operator, new(0, null), default);

        Assert.Equal(ManualPaymentStatuses.Paid, confirmed.Status);
        Assert.Equal(confirmed.Id, replay.Id);
        Assert.Equal(fixture.Ledger.Amount, fixture.Ledger.ConfirmedAmount);
        Assert.Equal(1, fixture.Store.Payments.Single().Version);
    }

    [Fact]
    public async Task Sales_cannot_write_manual_payment()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<ManualPaymentOperationException>(() =>
            fixture.Service.RecordAsync(fixture.Order.CreatedByUserId, SystemRoles.Sales,
                new(fixture.Order.Id, ManualPaymentMethods.CashOnDelivery,
                    fixture.Ledger.Amount, false, null), "manual-sales-001", default));

        Assert.Equal("forbidden", exception.Code);
    }

    [Fact]
    public async Task Manual_confirmation_is_blocked_while_payos_checkout_is_active()
    {
        var fixture = new Fixture(OrderPaymentMethods.Prepaid, delivered: false);
        fixture.Ledger.PayOsPaymentLinkId = "payos-active-link";
        fixture.Ledger.PayOsLinkStatus = "PENDING";

        var exception = await Assert.ThrowsAsync<ManualPaymentOperationException>(() =>
            fixture.RecordAsync("manual-after-payos-001", markPaid: true,
                method: ManualPaymentMethods.PrepaidTransfer));

        Assert.Equal("conflict", exception.Code);
        Assert.Equal(0, fixture.Ledger.ConfirmedAmount);
    }

    private sealed class Fixture
    {
        public Guid OperatorId { get; } = Guid.NewGuid();
        public Retailer Retailer { get; }
        public SalesOrder Order { get; }
        public Payment Ledger { get; }
        public FakeStore Store { get; }
        public ManualPaymentService Service { get; }

        public Fixture(string paymentMethod = OrderPaymentMethods.Cod, bool delivered = true)
        {
            Retailer = new Retailer
            {
                Id = Guid.NewGuid(),
                Code = "10004127",
                Name = "Cửa hàng An Phú"
            };
            Order = new SalesOrder
            {
                Id = Guid.NewGuid(),
                OrderCode = "DH-001041",
                RetailerId = Retailer.Id,
                RetailerCodeSnapshot = Retailer.Code,
                RetailerNameSnapshot = Retailer.Name,
                CreatedByUserId = Guid.NewGuid(),
                SalesGroupId = Guid.NewGuid(),
                PaymentMethod = paymentMethod,
                FinalAmount = 100_000m
            };
            Ledger = new Payment
            {
                Id = Guid.NewGuid(),
                SalesOrderId = Order.Id,
                Method = paymentMethod,
                Status = PaymentStatuses.Unpaid,
                Amount = Order.FinalAmount,
                AdjustedAmount = Order.FinalAmount,
                ConfirmedAmount = 0,
                FinalApprovedAt = Now.AddHours(-2),
                FinalApprovalReference = "RUN-20261020-01",
                DeliveredDate = delivered ? new DateOnly(2026, 10, 20) : null,
                Version = 0,
                CreatedAt = Now.AddHours(-2),
                UpdatedAt = Now.AddHours(-2)
            };
            Store = new FakeStore(Order, Retailer, Ledger);
            // Same here: the fake ledger knows no orders, so the accrual call does nothing.
            Service = new ManualPaymentService(Store, Store,
                new LoyaltyAccrualService(new FakeLoyaltyAccrualStore(), new FixedTimeProvider(Now)),
                new FixedTimeProvider(Now));
        }

        public Task<ManualPaymentResult> RecordAsync(
            string key, bool markPaid, string method = ManualPaymentMethods.CashOnDelivery) =>
            Service.RecordAsync(OperatorId, SystemRoles.Operator,
                new(Order.Id, method, Ledger.Amount, markPaid, "Ghi nhận thanh toán thủ công"),
                key, default);
    }

    private sealed class FakeStore(SalesOrder order, Retailer retailer, Payment ledger)
        : IManualPaymentStore, IPaymentStore
    {
        public List<ManualPayment> Payments { get; } = [];
        public List<AuditLog> Audits { get; } = [];

        public Task<SalesOrder?> FindOrderAsync(Guid salesOrderId, CancellationToken cancellationToken) =>
            Task.FromResult<SalesOrder?>(salesOrderId == order.Id ? order : null);

        public Task<ManualPayment?> FindForUpdateAsync(Guid paymentId, CancellationToken cancellationToken) =>
            Task.FromResult(Payments.SingleOrDefault(item => item.Id == paymentId));

        public Task<ManualPaymentView?> FindAsync(Guid paymentId, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Payments.SingleOrDefault(item => item.Id == paymentId)));

        public Task<ManualPaymentView?> FindByOperationKeyAsync(
            string operationKey, CancellationToken cancellationToken) =>
            Task.FromResult(ToView(Payments.SingleOrDefault(item => item.OperationKey == operationKey)));

        public Task<IReadOnlyCollection<ManualPaymentView>> ListByOrderAsync(
            Guid salesOrderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ManualPaymentView>>(Payments
                .Where(item => item.SalesOrderId == salesOrderId).Select(item => ToView(item)!).ToArray());

        public Task<bool> CanAccessOrderAsync(
            Guid userId, string role, SalesOrder target, CancellationToken cancellationToken) =>
            Task.FromResult(role is SystemRoles.Admin or SystemRoles.Operator ||
                role == SystemRoles.Sales && target.CreatedByUserId == userId);

        public void Add(ManualPayment payment) => Payments.Add(payment);
        public void AddAuditLog(AuditLog auditLog) => Audits.Add(auditLog);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        Task<PaymentView?> IPaymentStore.FindByOrderAsync(
            Guid orderId, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult<PaymentView?>(orderId == order.Id ? new(ledger, order, retailer) : null);

        Task<PaymentView?> IPaymentStore.FindByOperationKeyAsync(
            string operationKey, CancellationToken cancellationToken) => Task.FromResult<PaymentView?>(null);

        public Task<PaymentView?> FindByPayOsOrderCodeAsync(
            long payOsOrderCode, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult<PaymentView?>(null);

        public Task<IReadOnlyCollection<PaymentView>> ListAsync(
            Guid actorUserId, string actorRole, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PaymentView>>([new(ledger, order, retailer)]);

        public Task<bool> CanViewAsync(
            PaymentView view, Guid actorUserId, string actorRole, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public void Add(Payment payment) => throw new NotSupportedException();

        void IPaymentStore.AddAuditLog(AuditLog auditLog) => Audits.Add(auditLog);

        Task IPaymentStore.SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public void ClearTracking() { }

        private ManualPaymentView? ToView(ManualPayment? payment) => payment is null
            ? null
            : new(payment, order.OrderCode, ledger.Amount, ledger.ConfirmedAmount);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
