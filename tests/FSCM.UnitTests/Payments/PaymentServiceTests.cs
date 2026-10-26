using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Payments;

public sealed class PaymentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Final_approval_creates_ledger_and_delivery_sets_credit_due_date()
    {
        var fixture = new Fixture(OrderPaymentMethods.Credit, creditTermDays: 14, withPayment: false);

        await fixture.Plan.CreateForFinalApprovedOrderAsync(
            fixture.Order, fixture.RetailerUserId, Now.AddHours(-1), "RUN-20261015-01", default);
        await fixture.Plan.CreateForDeliveredOrderAsync(
            fixture.Order, fixture.RetailerUserId, Now, default);

        var payment = Assert.Single(fixture.Store.Payments);
        Assert.Equal(PaymentStatuses.Unpaid, payment.Status);
        Assert.Equal(new DateOnly(2026, 10, 29), payment.DueDate);
        Assert.Null(payment.ConfirmedByUserId);
        Assert.Equal(2, fixture.Store.Audits.Count);
        Assert.Equal("FinalPaymentLedgerCreated", fixture.Store.Audits[0].Action);
        Assert.Equal("DeliveryDateRecorded", fixture.Store.Audits[1].Action);
        Assert.Equal(fixture.RetailerUserId, fixture.Store.Audits[1].UserAccountId);
    }

    [Theory]
    [InlineData(OrderPaymentMethods.Cod)]
    [InlineData(OrderPaymentMethods.Prepaid)]
    public async Task Delivered_non_credit_order_is_not_settled_automatically(string method)
    {
        var fixture = new Fixture(method, creditTermDays: 0, withPayment: false);

        await fixture.Plan.CreateForFinalApprovedOrderAsync(
            fixture.Order, fixture.RetailerUserId, Now.AddHours(-1), "RUN-20261015-01", default);
        await fixture.Plan.CreateForDeliveredOrderAsync(
            fixture.Order, fixture.RetailerUserId, Now, default);

        var payment = Assert.Single(fixture.Store.Payments);
        Assert.Equal(PaymentStatuses.Unpaid, payment.Status);
        Assert.Equal(0, payment.ConfirmedAmount);
        Assert.Null(payment.CollectionMethod);
        Assert.Null(payment.ConfirmedByUserId);
        Assert.Equal("DeliveryDateRecorded", fixture.Store.Audits[1].Action);
    }

    [Fact]
    public async Task Sales_records_full_cash_collection_for_own_credit_order()
    {
        var fixture = new Fixture();

        var result = await fixture.CollectAsync(
            fixture.SalesUserId, SystemRoles.Sales, PaymentCollectionMethods.Cash);

        Assert.Equal(PaymentStatuses.Paid, result.Status);
        Assert.Equal(fixture.Order.FinalAmount, result.Amount);
        Assert.Equal(fixture.SalesUserId, result.ConfirmedByUserId);
        Assert.Equal(1, result.Version);
        var audit = Assert.Single(fixture.Store.Audits);
        Assert.Equal("Collected", audit.Action);
        Assert.Equal(fixture.SalesUserId, audit.UserAccountId);
        Assert.Contains("Thu đủ công nợ", audit.Reason);
    }

    [Fact]
    public async Task Operator_records_bank_transfer_collection()
    {
        var fixture = new Fixture();

        var result = await fixture.CollectAsync(
            Guid.NewGuid(), SystemRoles.Operator, PaymentCollectionMethods.BankTransfer);

        Assert.Equal(PaymentStatuses.Paid, result.Status);
        Assert.Equal(PaymentCollectionMethods.BankTransfer, result.CollectionMethod);
    }

    [Fact]
    public async Task Sales_cannot_record_bank_transfer()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<PaymentOperationException>(() => fixture.CollectAsync(
            fixture.SalesUserId, SystemRoles.Sales, PaymentCollectionMethods.BankTransfer));

        Assert.Equal("forbidden", exception.Code);
    }

    [Fact]
    public async Task Sales_cannot_collect_another_sales_order()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<PaymentOperationException>(() => fixture.CollectAsync(
            Guid.NewGuid(), SystemRoles.Sales, PaymentCollectionMethods.Cash));

        Assert.Equal("forbidden", exception.Code);
    }

    [Fact]
    public async Task Past_due_unpaid_receivable_is_reported_as_overdue()
    {
        var fixture = new Fixture();
        fixture.Payment!.DueDate = new DateOnly(2026, 10, 12);

        var result = Assert.Single(await fixture.Service.ListAsync(
            fixture.SalesUserId, SystemRoles.Sales, PaymentStatuses.Overdue, default));

        Assert.Equal(PaymentStatuses.Overdue, result.Status);
        Assert.Equal(3, result.DaysOverdue);
    }

    [Fact]
    public async Task Repeated_collection_key_is_idempotent_and_payload_change_conflicts()
    {
        var fixture = new Fixture();
        var first = await fixture.CollectAsync(
            fixture.SalesUserId, SystemRoles.Sales, PaymentCollectionMethods.Cash);
        var repeated = await fixture.CollectAsync(
            fixture.SalesUserId, SystemRoles.Sales, PaymentCollectionMethods.Cash, expectedVersion: 0);

        Assert.Equal(first.Id, repeated.Id);
        var exception = await Assert.ThrowsAsync<PaymentOperationException>(() =>
            fixture.Service.CollectAsync(fixture.Order.Id,
                new(PaymentCollectionMethods.Cash, 1, "Nội dung khác"),
                "payment-test-001", fixture.SalesUserId, SystemRoles.Sales, default));
        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Collection_rejects_stale_version()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<PaymentOperationException>(() => fixture.CollectAsync(
            fixture.SalesUserId, SystemRoles.Sales, PaymentCollectionMethods.Cash, expectedVersion: 2));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Collection_is_blocked_while_order_has_an_unresolved_dispute()
    {
        var fixture = new Fixture();
        fixture.Store.HasOpenDispute = true;

        var exception = await Assert.ThrowsAsync<PaymentOperationException>(() => fixture.CollectAsync(
            fixture.SalesUserId, SystemRoles.Sales, PaymentCollectionMethods.Cash));

        Assert.Equal("conflict", exception.Code);
        Assert.Equal(PaymentStatuses.Unpaid, fixture.Payment!.Status);
    }

    [Fact]
    public async Task Scoped_user_creates_payos_qr_checkout_for_final_approved_prepaid_order()
    {
        var fixture = new Fixture(OrderPaymentMethods.Prepaid);

        var result = await fixture.Service.CreatePayOsCheckoutAsync(
            fixture.Order.Id, new(0), fixture.SalesUserId, SystemRoles.Sales, default);
        var replay = await fixture.Service.CreatePayOsCheckoutAsync(
            fixture.Order.Id, new(0), fixture.SalesUserId, SystemRoles.Sales, default);

        Assert.Equal(fixture.Payment!.Amount, result.Amount);
        Assert.Equal(result.PaymentLinkId, replay.PaymentLinkId);
        Assert.Equal("https://pay.payos.vn/web/test-link", result.CheckoutUrl);
        Assert.Equal("PAYOS-QR", result.QrCode);
        Assert.Equal("PENDING", result.Status);
        Assert.Equal(1, result.PaymentVersion);
        Assert.Equal("PayOsCheckoutCreated", Assert.Single(fixture.Store.Audits).Action);
    }

    [Fact]
    public async Task Verified_payos_webhook_records_bank_transfer_idempotently()
    {
        var fixture = new Fixture(OrderPaymentMethods.Prepaid);
        var checkout = await fixture.Service.CreatePayOsCheckoutAsync(
            fixture.Order.Id, new(0), fixture.SalesUserId, SystemRoles.Sales, default);
        var webhook = fixture.Gateway.CreateWebhook(checkout);

        var first = await fixture.Service.HandlePayOsWebhookAsync(webhook, default);
        var repeated = await fixture.Service.HandlePayOsWebhookAsync(webhook, default);

        Assert.True(first.Processed);
        Assert.True(repeated.Processed);
        Assert.Equal(PaymentStatuses.Paid, fixture.Payment!.Status);
        Assert.Equal(fixture.Payment.Amount, fixture.Payment.ConfirmedAmount);
        Assert.Equal(PaymentCollectionMethods.BankTransfer, fixture.Payment.CollectionMethod);
        Assert.Equal("FT-PAYOS-001", fixture.Payment.PayOsReference);
        Assert.Equal(2, fixture.Payment.Version);
        Assert.Equal(2, fixture.Store.Audits.Count);
        Assert.Equal("PayOsPaymentConfirmed", fixture.Store.Audits[1].Action);
    }

    [Fact]
    public async Task Manual_collection_is_blocked_after_payos_checkout_is_created()
    {
        var fixture = new Fixture(OrderPaymentMethods.Prepaid);
        await fixture.Service.CreatePayOsCheckoutAsync(
            fixture.Order.Id, new(0), fixture.SalesUserId, SystemRoles.Sales, default);

        var exception = await Assert.ThrowsAsync<PaymentOperationException>(() => fixture.CollectAsync(
            Guid.NewGuid(), SystemRoles.Operator, PaymentCollectionMethods.BankTransfer,
            expectedVersion: 1));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Approved_claim_reduces_credit_receivable_outstanding()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.AdjustReceivableAsync(
            fixture.Order.Id, new AdjustReceivableData(300_000, "Khiếu nại thiếu 2 thùng được duyệt", 0),
            "adjust-test-001", Guid.NewGuid(), SystemRoles.Operator, default);

        Assert.Equal(fixture.Order.FinalAmount - 300_000, result.AdjustedAmount);
        Assert.Equal(fixture.Order.FinalAmount - 300_000, result.OutstandingAmount);
        Assert.Equal("ReceivableAdjusted", Assert.Single(fixture.Store.Audits).Action);
    }

    [Fact]
    public async Task Rejected_claim_restores_a_prior_reduction()
    {
        var fixture = new Fixture();
        var reduced = await fixture.Service.AdjustReceivableAsync(
            fixture.Order.Id, new AdjustReceivableData(300_000, "Duyệt tạm", 0),
            "adjust-reduce", Guid.NewGuid(), SystemRoles.Operator, default);

        var restored = await fixture.Service.AdjustReceivableAsync(
            fixture.Order.Id, new AdjustReceivableData(-300_000, "Khiếu nại bị từ chối, khôi phục", reduced.Version),
            "adjust-restore", Guid.NewGuid(), SystemRoles.Operator, default);

        Assert.Equal(fixture.Order.FinalAmount, restored.AdjustedAmount);
    }

    [Fact]
    public async Task Adjustment_cannot_restore_above_approved_total()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<PaymentOperationException>(() =>
            fixture.Service.AdjustReceivableAsync(
                fixture.Order.Id, new AdjustReceivableData(-100_000, "Khôi phục quá mức", 0),
                "adjust-over", Guid.NewGuid(), SystemRoles.Operator, default));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Adjustment_cannot_reduce_below_collected_amount()
    {
        var fixture = new Fixture();
        // Collect in full first so ConfirmedAmount == AdjustedAmount.
        await fixture.CollectAsync(fixture.SalesUserId, SystemRoles.Sales, PaymentCollectionMethods.Cash);

        var exception = await Assert.ThrowsAsync<PaymentOperationException>(() =>
            fixture.Service.AdjustReceivableAsync(
                fixture.Order.Id, new AdjustReceivableData(100_000, "Giảm sau khi đã thu đủ", 1),
                "adjust-below", Guid.NewGuid(), SystemRoles.Operator, default));

        Assert.Equal("conflict", exception.Code);
    }

    [Fact]
    public async Task Adjustment_requires_admin_or_operator()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<PaymentOperationException>(() =>
            fixture.Service.AdjustReceivableAsync(
                fixture.Order.Id, new AdjustReceivableData(100_000, "Không đủ quyền", 0),
                "adjust-forbidden", fixture.SalesUserId, SystemRoles.Sales, default));

        Assert.Equal("forbidden", exception.Code);
    }

    private sealed class Fixture
    {
        public Guid SalesUserId { get; } = Guid.NewGuid();
        public Guid RetailerUserId { get; } = Guid.NewGuid();
        public Retailer Retailer { get; }
        public SalesOrder Order { get; }
        public Payment? Payment { get; }
        public FakePaymentStore Store { get; }
        public PaymentPlanService Plan { get; }
        public PaymentService Service { get; }
        public FakePayOsGateway Gateway { get; }

        public Fixture(
            string method = OrderPaymentMethods.Credit,
            int creditTermDays = 7,
            bool withPayment = true)
        {
            Retailer = new Retailer
            {
                Id = Guid.NewGuid(),
                UserAccountId = RetailerUserId,
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
                CreatedByUserId = SalesUserId,
                PaymentMethod = method,
                CreditTermDays = creditTermDays,
                FinalAmount = 2_318_400,
                Status = OrderStatuses.Delivered
            };
            Payment = withPayment ? new Payment
            {
                Id = Guid.NewGuid(),
                SalesOrderId = Order.Id,
                Method = method,
                Status = PaymentStatuses.Unpaid,
                Amount = Order.FinalAmount,
                AdjustedAmount = Order.FinalAmount,
                ConfirmedAmount = 0,
                FinalApprovedAt = Now.AddHours(-1),
                FinalApprovalReference = "RUN-20261015-01",
                DeliveredDate = new DateOnly(2026, 10, 15),
                DueDate = new DateOnly(2026, 10, 22),
                Version = 0,
                CreatedAt = Now,
                UpdatedAt = Now
            } : null;
            Store = new FakePaymentStore(this);
            Gateway = new FakePayOsGateway();
            Plan = new PaymentPlanService(Store);
            Service = new PaymentService(Store, Gateway, new FixedTimeProvider(Now));
        }

        public Task<PaymentResult> CollectAsync(
            Guid actorUserId,
            string role,
            string collectionMethod,
            int expectedVersion = 0) => Service.CollectAsync(Order.Id,
            new(collectionMethod, expectedVersion, "Thu đủ công nợ"),
            "payment-test-001", actorUserId, role, default);
    }

    private sealed class FakePaymentStore : IPaymentStore
    {
        private readonly Fixture fixture;
        public List<Payment> Payments { get; } = [];
        public List<AuditLog> Audits { get; } = [];
        public bool HasOpenDispute { get; set; }

        public FakePaymentStore(Fixture fixture)
        {
            this.fixture = fixture;
            if (fixture.Payment is not null) Payments.Add(fixture.Payment);
        }

        public Task<PaymentView?> FindByOrderAsync(Guid orderId, bool forUpdate,
            CancellationToken cancellationToken) => Task.FromResult<PaymentView?>(
            Payments.SingleOrDefault(item => item.SalesOrderId == orderId) is { } payment
                ? new(payment, fixture.Order, fixture.Retailer, HasOpenDispute: HasOpenDispute)
                : null);

        public Task<PaymentView?> FindByOperationKeyAsync(string operationKey,
            CancellationToken cancellationToken) => Task.FromResult<PaymentView?>(
            Payments.SingleOrDefault(item => item.OperationKey == operationKey) is { } payment
                ? new(payment, fixture.Order, fixture.Retailer)
                : null);

        public Task<PaymentView?> FindByPayOsOrderCodeAsync(long payOsOrderCode, bool forUpdate,
            CancellationToken cancellationToken) => Task.FromResult<PaymentView?>(
            Payments.SingleOrDefault(item => item.PayOsOrderCode == payOsOrderCode) is { } payment
                ? new(payment, fixture.Order, fixture.Retailer)
                : null);

        public Task<IReadOnlyCollection<PaymentView>> ListAsync(Guid actorUserId, string actorRole,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<PaymentView>>(
            Payments.Select(item => new PaymentView(item, fixture.Order, fixture.Retailer)).ToArray());

        public Task<bool> CanViewAsync(PaymentView view, Guid actorUserId, string actorRole,
            CancellationToken cancellationToken) => Task.FromResult(
            actorRole is SystemRoles.Admin or SystemRoles.Operator or SystemRoles.SalesManager ||
            actorRole == SystemRoles.Sales && view.Order.CreatedByUserId == actorUserId ||
            actorRole == SystemRoles.Retailer && view.Retailer.UserAccountId == actorUserId);

        public void Add(Payment payment) => Payments.Add(payment);
        public void AddAuditLog(AuditLog auditLog) => Audits.Add(auditLog);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
        public void ClearTracking() { }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakePayOsGateway : IPayOsGateway
    {
        public Task<PayOsLinkData> CreatePaymentLinkAsync(
            PayOsCreateLinkData data,
            CancellationToken cancellationToken) => Task.FromResult(new PayOsLinkData(
                data.OrderCode, data.Amount, "test-link", "https://pay.payos.vn/web/test-link",
                "PAYOS-QR", "PENDING", 1_797_000_000));

        public Task<PayOsVerifiedWebhook> VerifyWebhookAsync(
            PayOsWebhookEnvelope webhook,
            CancellationToken cancellationToken) => Task.FromResult(
                new PayOsVerifiedWebhook(webhook.Code, webhook.Success, webhook.Data));

        public PayOsWebhookEnvelope CreateWebhook(PayOsCheckoutResult checkout) => new(
            "00", "success", true,
            new(checkout.OrderCode, decimal.ToInt64(checkout.Amount), "FSCM DH-001041",
                "123456789", "FT-PAYOS-001", "2026-10-15 10:05:00", "VND",
                checkout.PaymentLinkId, "00", "Thành công", null, null, null, null, null, null),
            "valid-test-signature");
    }
}
