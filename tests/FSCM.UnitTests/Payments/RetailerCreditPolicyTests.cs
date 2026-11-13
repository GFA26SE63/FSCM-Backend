using FSCM.Application.Features.Payments;
using FSCM.Domain.Entities;

namespace FSCM.UnitTests.Payments;

public sealed class RetailerCreditPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Rejects_credit_when_outstanding_balance_would_exceed_limit()
    {
        var retailer = ApprovedRetailer(1_000_000m);
        var store = new FakeStore([CreditView(retailer, adjustedAmount: 800_000m)]);
        var policy = new RetailerCreditPolicy(store, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<PaymentOperationException>(() =>
            policy.EnsureEligibleAsync(retailer, 200_001m, default));
    }

    [Fact]
    public async Task Rejects_credit_when_any_unsettled_credit_receivable_is_overdue()
    {
        var retailer = ApprovedRetailer(1_000_000m);
        var store = new FakeStore([CreditView(retailer, adjustedAmount: 100_000m,
            dueDate: new DateOnly(2026, 11, 12))]);
        var policy = new RetailerCreditPolicy(store, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<PaymentOperationException>(() =>
            policy.EnsureEligibleAsync(retailer, 100_000m, default));
    }

    [Fact]
    public async Task Allows_credit_at_exactly_the_approved_limit_when_no_receivable_is_overdue()
    {
        var retailer = ApprovedRetailer(1_000_000m);
        var store = new FakeStore([CreditView(retailer, adjustedAmount: 800_000m,
            dueDate: new DateOnly(2026, 11, 13))]);
        var policy = new RetailerCreditPolicy(store, new FixedTimeProvider(Now));

        await policy.EnsureEligibleAsync(retailer, 200_000m, default);
    }

    private static Retailer ApprovedRetailer(decimal creditLimit) => new()
    {
        Id = Guid.NewGuid(), Status = "Active", CreditTermDays = 14, CreditLimit = creditLimit
    };

    private static PaymentView CreditView(Retailer retailer, decimal adjustedAmount, DateOnly? dueDate = null) => new(
        new Payment
        {
            Id = Guid.NewGuid(), SalesOrderId = Guid.NewGuid(), Method = "Credit", Status = PaymentStatuses.Unpaid,
            Amount = adjustedAmount, AdjustedAmount = adjustedAmount, DueDate = dueDate
        },
        new SalesOrder { Id = Guid.NewGuid(), RetailerId = retailer.Id }, retailer);

    private sealed class FakeStore(IReadOnlyCollection<PaymentView> creditPayments) : IPaymentStore
    {
        public Task<PaymentView?> FindByOrderAsync(Guid orderId, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult<PaymentView?>(null);
        public Task<PaymentView?> FindByOperationKeyAsync(string operationKey, CancellationToken cancellationToken) =>
            Task.FromResult<PaymentView?>(null);
        public Task<PaymentView?> FindByPayOsOrderCodeAsync(long payOsOrderCode, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult<PaymentView?>(null);
        public Task<IReadOnlyCollection<PaymentView>> ListAsync(Guid actorUserId, string actorRole, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<PaymentView>>([]);
        public Task<IReadOnlyCollection<PaymentView>> ListCreditForRetailerAsync(Guid retailerId, CancellationToken cancellationToken) =>
            Task.FromResult(creditPayments);
        public Task<bool> CanViewAsync(PaymentView view, Guid actorUserId, string actorRole, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public void Add(Payment payment) { }
        public void AddReceipt(PaymentReceipt receipt, PaymentReceiptAllocation allocation) { }
        public void AddAuditLog(AuditLog auditLog) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
        public void ClearTracking() { }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
