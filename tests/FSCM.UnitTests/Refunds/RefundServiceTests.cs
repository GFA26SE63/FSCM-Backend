using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Refunds;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Refunds;

public sealed class RefundServiceTests
{
    [Fact]
    public async Task Operator_submits_external_transfer_and_different_admin_verifies_it()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync(40_000, "refund-create-1");
        Assert.Equal(RefundStatuses.PendingRefund, created.Status);
        Assert.Equal(60_000, created.RefundDueAmount);

        var submitted = await fixture.Service.SubmitEvidenceAsync(created.Id,
            new(created.Version, fixture.Now.AddMinutes(-5), "TXN-001", "private://refunds/evidence-001"),
            "refund-evidence-1", fixture.OperatorId, SystemRoles.Operator, default);
        Assert.Equal(RefundStatuses.AwaitingVerification, submitted.Status);

        var verified = await fixture.Service.VerifyAsync(created.Id,
            new(submitted.Version, "Đã đối chiếu sao kê"), "refund-verify-1",
            fixture.AdminId, SystemRoles.Admin, default);
        Assert.Equal(RefundStatuses.Refunded, verified.Status);
        Assert.Equal(40_000, verified.RefundedAmount);
        Assert.Equal(20_000, verified.RefundDueAmount);
    }

    [Fact]
    public async Task Pending_refunds_reserve_cap_and_prevent_over_refund()
    {
        var fixture = new Fixture();
        await fixture.CreateAsync(40_000, "refund-create-2");
        var error = await Assert.ThrowsAsync<RefundOperationException>(() =>
            fixture.CreateAsync(30_000, "refund-create-3"));
        Assert.Equal("conflict", error.Code);
    }

    [Fact]
    public async Task Idempotency_key_replays_same_create_and_rejects_changed_payload()
    {
        var fixture = new Fixture();
        var first = await fixture.CreateAsync(40_000, "refund-create-4");
        var replay = await fixture.CreateAsync(40_000, "refund-create-4");
        Assert.Equal(first.Id, replay.Id);
        await Assert.ThrowsAsync<RefundOperationException>(() =>
            fixture.CreateAsync(20_000, "refund-create-4"));
    }

    [Fact]
    public async Task Evidence_submitter_cannot_verify_same_refund_even_with_admin_role()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync(40_000, "refund-create-5");
        var submitted = await fixture.Service.SubmitEvidenceAsync(created.Id,
            new(created.Version, fixture.Now.AddMinutes(-1), "TXN-005", "private://refunds/evidence-005"),
            "refund-evidence-5", fixture.OperatorId, SystemRoles.Operator, default);
        var error = await Assert.ThrowsAsync<RefundOperationException>(() => fixture.Service.VerifyAsync(
            created.Id, new(submitted.Version, null), "refund-verify-5",
            fixture.OperatorId, SystemRoles.Admin, default));
        Assert.Equal("forbidden", error.Code);
    }

    private sealed class Fixture
    {
        public DateTimeOffset Now { get; } = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);
        public Guid OperatorId { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public RefundService Service { get; }
        private FakeStore Store { get; }

        public Fixture()
        {
            Store = new FakeStore(Now);
            Service = new RefundService(Store, Store, new FixedTimeProvider(Now));
        }

        public Task<RefundResult> CreateAsync(decimal amount, string key) => Service.CreateAsync(
            new(Store.Order.Id, amount, "Cửa hàng An Khang", "Điều chỉnh nghĩa vụ hợp lệ"),
            key, OperatorId, SystemRoles.Operator, default);
    }

    private sealed class FakeStore : IRefundStore, IPaymentStore
    {
        public SalesOrder Order { get; }
        private Retailer Retailer { get; }
        private Payment Payment { get; }
        private List<ManualRefund> Refunds { get; } = [];
        private List<AuditLog> Audits { get; } = [];

        public FakeStore(DateTimeOffset now)
        {
            Retailer = new Retailer { Id = Guid.NewGuid(), UserAccountId = Guid.NewGuid(), Code = "CH-001", Name = "An Khang" };
            Order = new SalesOrder
            {
                Id = Guid.NewGuid(),
                OrderCode = "DH-001100",
                RetailerId = Retailer.Id,
                CreatedByUserId = Guid.NewGuid(),
                SalesGroupId = Guid.NewGuid()
            };
            Payment = new Payment
            {
                Id = Guid.NewGuid(),
                SalesOrderId = Order.Id,
                Amount = 100_000,
                AdjustedAmount = 40_000,
                ConfirmedAmount = 100_000,
                Method = "Prepaid",
                Status = PaymentStatuses.Paid,
                FinalApprovedAt = now.AddDays(-1),
                FinalApprovalReference = "RUN-001",
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        public Task<RefundView?> FindAsync(Guid id, bool forUpdate, CancellationToken ct)
        {
            var refund = Refunds.SingleOrDefault(x => x.Id == id);
            return Task.FromResult(refund is null ? null : View(refund));
        }
        Task<RefundView?> IRefundStore.FindByOperationKeyAsync(string key, CancellationToken ct)
        {
            var refund = Refunds.SingleOrDefault(x => x.CreateOperationKey == key || x.EvidenceOperationKey == key || x.VerificationOperationKey == key);
            return Task.FromResult(refund is null ? null : View(refund));
        }
        public Task<IReadOnlyCollection<RefundView>> ListAsync(Guid? orderId, string? status, Guid actor, string role, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<RefundView>>(Refunds.Where(x => (!orderId.HasValue || x.SalesOrderId == orderId) && (status is null || x.Status == status)).Select(View).ToArray());
        public Task<(decimal Pending, decimal Refunded)> TotalsAsync(Guid paymentId, CancellationToken ct) =>
            Task.FromResult((Refunds.Where(x => x.Status is RefundStatuses.PendingRefund or RefundStatuses.AwaitingVerification).Sum(x => x.Amount),
                Refunds.Where(x => x.Status == RefundStatuses.Refunded).Sum(x => x.Amount)));
        public void Add(ManualRefund refund) => Refunds.Add(refund);
        public void AddAuditLog(AuditLog audit) => Audits.Add(audit);
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

        Task<PaymentView?> IPaymentStore.FindByOrderAsync(Guid orderId, bool forUpdate, CancellationToken ct) =>
            Task.FromResult<PaymentView?>(orderId == Order.Id ? new(Payment, Order, Retailer) : null);
        public Task<PaymentView?> FindByOperationKeyAsync(string key, CancellationToken ct) => Task.FromResult<PaymentView?>(null);
        public Task<PaymentView?> FindByPayOsOrderCodeAsync(long code, bool forUpdate, CancellationToken ct) => Task.FromResult<PaymentView?>(null);
        public Task<IReadOnlyCollection<PaymentView>> ListAsync(Guid actor, string role, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<PaymentView>>([]);
        public Task<bool> CanViewAsync(PaymentView view, Guid actor, string role, CancellationToken ct) => Task.FromResult(true);
        public void Add(Payment payment) { }
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct) => action(ct);
        public void ClearTracking() { }

        private RefundView View(ManualRefund refund)
        {
            var totals = TotalsAsync(Payment.Id, default).Result;
            return new(refund, Payment, Order, Retailer, totals.Pending, totals.Refunded);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
