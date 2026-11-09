using FSCM.Application.Features.Payments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Payments;

public sealed class PaymentReceiptProposalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 2);

    [Fact]
    public async Task Proposal_orders_overdue_then_due_then_created_and_caps_at_outstanding()
    {
        var retailer = Guid.NewGuid();
        var overdue = Receivable("DH-003", 100_000, due: Today.AddDays(-2), created: Now.AddDays(-10));
        var dueSoon = Receivable("DH-001", 100_000, due: Today.AddDays(3), created: Now.AddDays(-1));
        var dueLater = Receivable("DH-002", 100_000, due: Today.AddDays(9), created: Now.AddDays(-5));
        var store = new FakeStore(retailer, [dueSoon, dueLater, overdue]);
        var service = new PaymentReceiptService(store, new Clock());

        // 250k received: fills overdue (100k) then earliest due (100k) then part of the next (50k).
        var result = await service.ProposeAllocationAsync(
            new ProposeReceiptAllocationData(retailer, 250_000), Guid.NewGuid(), SystemRoles.Operator, default);

        Assert.Equal(["DH-003", "DH-001", "DH-002"], result.Allocations.Select(x => x.OrderCode));
        Assert.Equal([100_000m, 100_000m, 50_000m], result.Allocations.Select(x => x.ProposedAmount));
        Assert.True(result.Allocations.First().IsOverdue);
        Assert.Equal(250_000m, result.AllocatedAmount);
        Assert.Equal(0m, result.UnappliedCreditAmount);
    }

    [Fact]
    public async Task Proposal_reports_surplus_as_unapplied_credit()
    {
        var retailer = Guid.NewGuid();
        var only = Receivable("DH-010", 60_000, due: Today.AddDays(5), created: Now);
        var service = new PaymentReceiptService(new FakeStore(retailer, [only]), new Clock());

        var result = await service.ProposeAllocationAsync(
            new ProposeReceiptAllocationData(retailer, 100_000), Guid.NewGuid(), SystemRoles.Operator, default);

        Assert.Equal(60_000m, result.AllocatedAmount);
        Assert.Equal(40_000m, result.UnappliedCreditAmount);
    }

    [Fact]
    public async Task Proposal_requires_admin_or_operator()
    {
        var service = new PaymentReceiptService(new FakeStore(Guid.NewGuid(), []), new Clock());
        var error = await Assert.ThrowsAsync<PaymentOperationException>(() =>
            service.ProposeAllocationAsync(
                new ProposeReceiptAllocationData(Guid.NewGuid(), 10_000), Guid.NewGuid(), SystemRoles.Sales, default));
        Assert.Equal("forbidden", error.Code);
    }

    private static OutstandingReceivableView Receivable(
        string code, decimal outstanding, DateOnly due, DateTimeOffset created) =>
        new(Guid.NewGuid(), Guid.NewGuid(), code, outstanding, due, created, due < Today);

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    private sealed class FakeStore(Guid retailerId, OutstandingReceivableView[] rows) : IPaymentReceiptStore
    {
        public Task<IReadOnlyCollection<OutstandingReceivableView>> ListOutstandingReceivablesByRetailerAsync(
            Guid retailer, DateOnly today, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<OutstandingReceivableView>>(
                retailer == retailerId ? rows : []);

        // Unused by the proposal path.
        public Task<PaymentReceipt?> FindReceiptByOperationKeyAsync(string k, bool u, CancellationToken ct) => Task.FromResult<PaymentReceipt?>(null);
        public Task<PaymentReceipt?> FindReceiptAsync(Guid id, bool u, CancellationToken ct) => Task.FromResult<PaymentReceipt?>(null);
        public Task<IReadOnlyCollection<PaymentReceiptAllocation>> ListAllocationsAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<PaymentReceiptAllocation>>([]);
        public Task<IReadOnlyCollection<ReceiptPaymentView>> FindPaymentsForUpdateAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<ReceiptPaymentView>>([]);
        public Task<decimal> ConfirmedAllocationTotalAsync(Guid id, Guid? excluded, CancellationToken ct) => Task.FromResult(0m);
    public Task<bool> ReferenceInUseAsync(Guid retailerId, string reference, CancellationToken ct) => Task.FromResult(false);
    public Task<PaymentReceiptAllocation?> FindAllocationForUpdateAsync(Guid receiptId, Guid paymentId, CancellationToken ct) => Task.FromResult<PaymentReceiptAllocation?>(null);
        public void AddReceipt(PaymentReceipt item) { }
        public void AddAllocations(IEnumerable<PaymentReceiptAllocation> items) { }
        public void AddAuditLog(AuditLog item) { }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct) => operation(ct);
        public void ClearTracking() { }
    }
}
