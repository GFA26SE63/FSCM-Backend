using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Application.Features.Quotes;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using Xunit;

namespace FSCM.UnitTests.Quotes;

public sealed class QuoteServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-08T10:00:00Z");

    [Fact]
    public async Task AcceptAsync_updates_order_final_amount_and_creates_payment_ledger()
    {
        var quoteStore = new FakeQuoteStore();
        var paymentPlanService = new FakePaymentPlanService();
        var orderId = Guid.NewGuid();
        var quoteId = Guid.NewGuid();
        var retailerUserId = Guid.NewGuid();

        var order = new SalesOrder
        {
            Id = orderId,
            OrderCode = "DH-20261108-01",
            RetailerId = Guid.NewGuid(),
            PaymentMethod = OrderPaymentMethods.Credit,
            CreditTermDays = 14,
            FinalAmount = 100_000m,
            FinalPricePendingAllocation = true,
            Version = 1
        };

        var quote = new SalesOrderQuote
        {
            Id = quoteId,
            SalesOrderId = orderId,
            AllocationRunId = Guid.NewGuid(),
            AllocationPlanVersionId = Guid.NewGuid(),
            RevisionNumber = 1,
            Status = SalesOrderQuoteStatuses.PendingRetailerAcceptance,
            TotalAmount = 90_000m, // Discounted by 10%
            IssuedAt = Now.AddHours(-1),
            ExpiresAt = Now.AddHours(23),
            Version = 0
        };

        quoteStore.Views.Add(new QuoteView(quote, order, null));

        var service = new QuoteService(quoteStore, new FixedClock(), null, paymentPlanService);

        var result = await service.AcceptAsync(
            quoteId, 0, retailerUserId, SystemRoles.Retailer, CancellationToken.None);

        Assert.Equal(SalesOrderQuoteStatuses.Accepted, result.Status);
        Assert.NotNull(result.PurchaseOrderCode);
        Assert.Equal(90_000m, order.FinalAmount);
        Assert.False(order.FinalPricePendingAllocation);

        var createdPayment = Assert.Single(paymentPlanService.CreatedPayments);
        Assert.Equal(orderId, createdPayment.Order.Id);
        Assert.Equal(90_000m, createdPayment.Order.FinalAmount);
        Assert.Equal(result.PurchaseOrderCode, createdPayment.FinalApprovalReference);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeQuoteStore : IQuoteStore
    {
        public List<QuoteView> Views { get; } = [];
        public List<PurchaseOrder> PurchaseOrders { get; } = [];

        public Task<QuoteView?> FindAsync(Guid id, bool forUpdate, CancellationToken cancellationToken) =>
            Task.FromResult(Views.FirstOrDefault(x => x.Quote.Id == id));

        public Task<IReadOnlyCollection<QuoteView>> ListForOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<QuoteView>>(Views.Where(x => x.Order.Id == orderId).ToArray());

        public Task<IReadOnlyCollection<QuoteView>> ListForRunAsync(Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<QuoteView>>(Views);

        public Task<bool> CanRetailerAccessAsync(Guid quoteId, Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyCollection<QuoteIssueOrder>> ListOrdersForPlanAsync(Guid allocationPlanVersionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<QuoteIssueOrder>>([]);

        public void Add(SalesOrderQuote quote) { }
        public void AddPurchaseOrder(PurchaseOrder purchaseOrder)
        {
            PurchaseOrders.Add(purchaseOrder);
            var view = Views.FirstOrDefault(x => x.Quote.Id == purchaseOrder.SalesOrderQuoteId);
            if (view is not null)
            {
                Views.Remove(view);
                Views.Add(new QuoteView(view.Quote, view.Order, purchaseOrder));
            }
        }
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePaymentPlanService : IPaymentPlanService
    {
        public record CreatedPaymentCall(SalesOrder Order, Guid ActorId, DateTimeOffset ApprovedAt, string FinalApprovalReference);
        public List<CreatedPaymentCall> CreatedPayments { get; } = [];

        public Task CreateForFinalApprovedOrderAsync(
            SalesOrder order,
            Guid approvalActorUserId,
            DateTimeOffset approvedAt,
            string finalApprovalReference,
            CancellationToken cancellationToken)
        {
            CreatedPayments.Add(new CreatedPaymentCall(order, approvalActorUserId, approvedAt, finalApprovalReference));
            return Task.CompletedTask;
        }

        public Task CreateForDeliveredOrderAsync(
            SalesOrder order,
            Guid receiptActorUserId,
            DateTimeOffset deliveredAt,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

