using FSCM.Application.Features.Inventory;
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

    [Fact]
    public async Task Expiration_releases_abandoned_quote_stock_without_retailer_action()
    {
        var quotes = new FakeQuoteStore();
        var order = new SalesOrder { Id = Guid.NewGuid(), CreatedByUserId = Guid.NewGuid() };
        var quote = new SalesOrderQuote { Id = Guid.NewGuid(), SalesOrderId = order.Id,
            Status = SalesOrderQuoteStatuses.PendingRetailerAcceptance, ExpiresAt = Now.AddMinutes(-1) };
        quotes.Views.Add(new(quote, order, null));
        var releases = new FSCM.UnitTests.Orders.FakeOrderReleaseStore();
        var balance = new InventoryBalance { PhysicalQuantity = 20m, ReservedQuantity = 5m, AvailableQuantity = 15m };
        var reservation = new Reservation { Quantity = 5m, Status = ReservationStatuses.Active };
        releases.Held.Add(new(Guid.NewGuid(), reservation, balance, new Batch { Id = Guid.NewGuid() }));
        var service = new QuoteService(quotes, new FixedClock(), new OrderReleaseService(releases, new FixedClock()));
        await service.ExpirePendingAsync(default);
        Assert.Equal(SalesOrderQuoteStatuses.Expired, quote.Status);
        Assert.Equal(0m, balance.ReservedQuantity);
        Assert.Equal(20m, balance.AvailableQuantity);
        Assert.Equal(ReservationStatuses.Released, reservation.Status);
        await service.ExpirePendingAsync(default);
        Assert.Equal(0m, balance.ReservedQuantity);
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

        public Task<IReadOnlyCollection<Guid>> ListExpiredPendingIdsAsync(DateTimeOffset now, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<Guid>>(Views.Where(x => x.Quote.Status == SalesOrderQuoteStatuses.PendingRetailerAcceptance && x.Quote.ExpiresAt < now).Select(x => x.Quote.Id).ToArray());
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

