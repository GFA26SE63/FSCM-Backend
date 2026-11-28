using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Refunds;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Refunds;

public sealed class RefundServiceTests
{
    [Fact]
    public async Task UT_Refund_Service_UTCID01_creates_a_claim_linked_obligation_at_the_approved_batch_net_loss()
    {
        var fixture = new Fixture();
        var claimId = Guid.NewGuid();
        const decimal batchFinalNetUnitPrice = 23_000m;
        const decimal damagedQuantity = 2m;
        fixture.RegisterApprovedClaim(claimId, damagedQuantity * batchFinalNetUnitPrice);

        var listPriceAttempt = await Assert.ThrowsAsync<RefundOperationException>(() =>
            fixture.CreateForClaimAsync(claimId, 60_000m, "refund-list-price-1"));
        Assert.Equal("conflict", listPriceAttempt.Code);

        var created = await fixture.CreateForClaimAsync(claimId,
            damagedQuantity * batchFinalNetUnitPrice, "refund-batch-net-1");

        Assert.Equal(46_000m, created.Amount);
        Assert.Equal(claimId, created.ReceiptDiscrepancyId);
        Assert.Equal(RefundStatuses.PendingRefund, created.Status);
        Assert.Equal(60_000m, created.RefundDueAmount);
    }

    [Fact]
    public async Task UT_Refund_Service_UTCID02_external_transfer_evidence_awaits_independent_verification()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync(40_000, "refund-create-1");
        Assert.Equal(RefundStatuses.PendingRefund, created.Status);
        Assert.Equal(60_000, created.RefundDueAmount);

        var submitted = await fixture.Service.SubmitEvidenceAsync(created.Id,
            new(created.Version, fixture.Now.AddMinutes(-5), "TXN-001", "private://refunds/evidence-001"),
            "refund-evidence-1", fixture.OperatorId, SystemRoles.Operator, default);
        Assert.Equal(RefundStatuses.AwaitingVerification, submitted.Status);
        Assert.Equal("TXN-001", submitted.TransactionReference);
        Assert.Equal("private://refunds/evidence-001", submitted.EvidenceReference);
        Assert.Equal(0m, submitted.RefundedAmount);
        Assert.Null(submitted.VerifiedAt);
    }

    [Fact]
    public async Task UT_Refund_Service_UTCID03_independent_admin_verification_is_timestamped_and_replayable_once()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync(40_000, "refund-create-verify");
        var submitted = await fixture.Service.SubmitEvidenceAsync(created.Id,
            new(created.Version, fixture.Now.AddMinutes(-5), "TXN-VERIFY", "private://refunds/evidence-verify"),
            "refund-evidence-verify", fixture.OperatorId, SystemRoles.Operator, default);

        var verified = await fixture.Service.VerifyAsync(created.Id,
            new(submitted.Version, true, "Đã đối chiếu sao kê"), "refund-verify-1",
            fixture.AdminId, SystemRoles.Admin, default);
        var replay = await fixture.Service.VerifyAsync(created.Id,
            new(submitted.Version, true, "Đã đối chiếu sao kê"), "refund-verify-1",
            fixture.AdminId, SystemRoles.Admin, default);

        Assert.Equal(RefundStatuses.Refunded, verified.Status);
        Assert.Equal(40_000, verified.RefundedAmount);
        Assert.Equal(20_000, verified.RefundDueAmount);
        Assert.NotNull(verified.VerifiedAt);
        Assert.Equal(fixture.Now, verified.VerifiedAt.Value);
        Assert.Equal("private://refunds/evidence-verify", verified.EvidenceReference);
        Assert.Equal(verified.Id, replay.Id);
        Assert.Equal(verified.Version, replay.Version);
        Assert.Equal(3, fixture.AuditCount);
    }

    [Fact]
    public async Task UT_Refund_Service_UTCID04_pending_refunds_reserve_cap_and_prevent_over_refund()
    {
        var fixture = new Fixture();
        await fixture.CreateAsync(40_000, "refund-create-2");
        var error = await Assert.ThrowsAsync<RefundOperationException>(() =>
            fixture.CreateAsync(30_000, "refund-create-3"));
        Assert.Equal("conflict", error.Code);
    }

    [Fact]
    public async Task UT_Refund_Service_UTCID04_idempotency_key_replays_same_create_and_rejects_changed_payload()
    {
        var fixture = new Fixture();
        var first = await fixture.CreateAsync(40_000, "refund-create-4");
        var replay = await fixture.CreateAsync(40_000, "refund-create-4");
        Assert.Equal(first.Id, replay.Id);
        await Assert.ThrowsAsync<RefundOperationException>(() =>
            fixture.CreateAsync(20_000, "refund-create-4"));
    }

    [Fact]
    public async Task Non_admin_evidence_submitter_cannot_verify_same_refund()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync(40_000, "refund-create-5");
        var submitted = await fixture.Service.SubmitEvidenceAsync(created.Id,
            new(created.Version, fixture.Now.AddMinutes(-1), "TXN-005", "private://refunds/evidence-005"),
            "refund-evidence-5", fixture.OperatorId, SystemRoles.Operator, default);
        var error = await Assert.ThrowsAsync<RefundOperationException>(() => fixture.Service.VerifyAsync(
            created.Id, new(submitted.Version, true, null), "refund-verify-5",
            fixture.OperatorId, SystemRoles.Operator, default));
        Assert.Equal("forbidden", error.Code);
    }

    [Fact]
    public async Task Admin_can_create_submit_and_verify_own_refund_with_audit_and_idempotency()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync(40_000, "admin-create", fixture.AdminId, SystemRoles.Admin);
        var submitted = await fixture.Service.SubmitEvidenceAsync(created.Id,
            new(created.Version, fixture.Now.AddMinutes(-1), "TXN-ADMIN", "private://refunds/admin"),
            "admin-evidence", fixture.AdminId, SystemRoles.Admin, default);
        var verified = await fixture.Service.VerifyAsync(created.Id,
            new(submitted.Version, true, "Admin đã đối chiếu"), "admin-verify", fixture.AdminId, SystemRoles.Admin, default);
        var replay = await fixture.Service.VerifyAsync(created.Id,
            new(submitted.Version, true, "Admin đã đối chiếu"), "admin-verify", fixture.AdminId, SystemRoles.Admin, default);
        Assert.Equal(RefundStatuses.Refunded, verified.Status);
        Assert.Equal(40_000, verified.RefundedAmount);
        Assert.Equal(fixture.Now, verified.VerifiedAt);
        Assert.Equal(verified.Version, replay.Version);
        Assert.Equal(3, fixture.AuditCount);
    }

    [Fact]
    public async Task Admin_rejects_bad_evidence_and_operator_can_resubmit()
    {
        var fixture = new Fixture();
        var created = await fixture.CreateAsync(40_000, "refund-create-6");
        var submitted = await fixture.Service.SubmitEvidenceAsync(created.Id,
            new(created.Version, fixture.Now.AddMinutes(-1), "TXN-WRONG", "private://refunds/wrong"),
            "refund-evidence-6", fixture.OperatorId, SystemRoles.Operator, default);

        var rejected = await fixture.Service.VerifyAsync(created.Id,
            new(submitted.Version, false, "Sai số tham chiếu ngân hàng"), "refund-reject-6",
            fixture.AdminId, SystemRoles.Admin, default);
        var resubmitted = await fixture.Service.SubmitEvidenceAsync(created.Id,
            new(rejected.Version, fixture.Now.AddMinutes(-1), "TXN-CORRECT", "private://refunds/correct"),
            "refund-evidence-6b", fixture.OperatorId, SystemRoles.Operator, default);

        // F5 step 18: rejection moves to EvidenceRejected and KEEPS the prior evidence for history
        // (the obligation is not destroyed); the Operator then resubmits corrected evidence.
        Assert.Equal(RefundStatuses.EvidenceRejected, rejected.Status);
        Assert.Equal("TXN-WRONG", rejected.TransactionReference);
        Assert.Equal("Sai số tham chiếu ngân hàng", rejected.VerificationNote);
        Assert.Equal(RefundStatuses.AwaitingVerification, resubmitted.Status);
        Assert.Equal("TXN-CORRECT", resubmitted.TransactionReference);
        Assert.Null(resubmitted.VerificationNote);
    }

    private sealed class Fixture
    {
        public DateTimeOffset Now { get; } = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);
        public Guid OperatorId { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public RefundService Service { get; }
        private FakeStore Store { get; }
        public int AuditCount => Store.AuditCount;

        public Fixture()
        {
            Store = new FakeStore(Now);
            Service = new RefundService(Store, Store, new FixedTimeProvider(Now));
        }

        public Task<RefundResult> CreateAsync(decimal amount, string key, Guid? actorId = null, string role = SystemRoles.Operator) => Service.CreateAsync(
            new(Store.Order.Id, amount, "Cửa hàng An Khang", "Điều chỉnh nghĩa vụ hợp lệ"),
            key, actorId ?? OperatorId, role, default);

        public void RegisterApprovedClaim(Guid claimId, decimal adjustmentAmount) =>
            Store.Claims[claimId] = new RefundClaimSource(claimId, Store.Order.Id, true,
                "FinancialAdjustment", adjustmentAmount, 0m);

        public Task<RefundResult> CreateForClaimAsync(Guid claimId, decimal amount, string key) =>
            Service.CreateAsync(new(Store.Order.Id, amount, "Cửa hàng An Khang",
                "Hoàn tiền cho lô hàng hư hỏng", claimId), key, OperatorId, SystemRoles.Operator, default);
    }

    private sealed class FakeStore : IRefundStore, IPaymentStore
    {
        public SalesOrder Order { get; }
        private Retailer Retailer { get; }
        private Payment Payment { get; }
        private List<ManualRefund> Refunds { get; } = [];
        private List<AuditLog> Audits { get; } = [];
        public int AuditCount => Audits.Count;

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
        public Dictionary<Guid, RefundClaimSource> Claims { get; } = [];

        public Task<RefundClaimSource?> FindClaimSourceAsync(Guid claimId, CancellationToken ct)
        {
            if (!Claims.TryGetValue(claimId, out var claim)) return Task.FromResult<RefundClaimSource?>(null);
            var linked = Refunds.Where(x => x.ReceiptDiscrepancyId == claimId).Sum(x => x.Amount);
            return Task.FromResult<RefundClaimSource?>(claim with { AlreadyLinkedAmount = linked });
        }

        public Task<IReadOnlyCollection<RefundObligationResult>> ListObligationsAsync(Guid actorUserId, string actorRole, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<RefundObligationResult>>([]);

        public Task<(decimal Pending, decimal Refunded)> TotalsAsync(Guid paymentId, CancellationToken ct) =>
            Task.FromResult((Refunds.Where(x => x.Status is RefundStatuses.PendingRefund or RefundStatuses.AwaitingVerification or RefundStatuses.EvidenceRejected).Sum(x => x.Amount),
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
        public void AddReceipt(PaymentReceipt receipt, PaymentReceiptAllocation allocation) { }
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
