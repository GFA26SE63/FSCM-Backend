using FSCM.Domain.Entities;

namespace FSCM.Application.Features.Quotes;

public sealed record QuoteView(SalesOrderQuote Quote, SalesOrder Order, PurchaseOrder? PurchaseOrder);

public interface IQuoteStore
{
    Task<QuoteView?> FindAsync(Guid id, bool forUpdate, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<QuoteView>> ListForOrderAsync(Guid orderId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<QuoteView>> ListForRunAsync(Guid runId, CancellationToken cancellationToken);
    Task<bool> CanRetailerAccessAsync(Guid quoteId, Guid userId, CancellationToken cancellationToken);
    Task<Payment?> FindPaymentByOrderAsync(Guid orderId, CancellationToken cancellationToken);
    void Add(SalesOrderQuote quote);
    void AddPurchaseOrder(PurchaseOrder purchaseOrder);
    void AddPayment(Payment payment);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
