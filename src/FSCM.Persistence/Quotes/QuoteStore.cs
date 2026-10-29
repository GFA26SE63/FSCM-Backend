using FSCM.Application.Features.Quotes;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Quotes;

public sealed class QuoteStore(AppDbContext db) : IQuoteStore
{
    public Task<QuoteView?> FindAsync(Guid id, bool forUpdate, CancellationToken ct)
    {
        var quotes = forUpdate ? db.SalesOrderQuoteRecords : db.SalesOrderQuoteRecords.AsNoTracking();
        return (from quote in quotes
                join order in db.SalesOrderRecords.AsNoTracking() on quote.SalesOrderId equals order.Id
                join po0 in db.PurchaseOrderRecords.AsNoTracking() on quote.PurchaseOrderId equals po0.Id into pos
                from po in pos.DefaultIfEmpty()
                where quote.Id == id
                select new QuoteView(quote, order, po)).SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyCollection<QuoteView>> ListForOrderAsync(Guid orderId, CancellationToken ct) =>
        await Build(db.SalesOrderQuoteRecords.AsNoTracking().Where(x => x.SalesOrderId == orderId)).ToArrayAsync(ct);

    public async Task<IReadOnlyCollection<QuoteView>> ListForRunAsync(Guid runId, CancellationToken ct) =>
        await Build(db.SalesOrderQuoteRecords.AsNoTracking().Where(x => x.AllocationRunId == runId)).ToArrayAsync(ct);

    public Task<bool> CanRetailerAccessAsync(Guid quoteId, Guid userId, CancellationToken ct) =>
        (from quote in db.SalesOrderQuoteRecords
         join order in db.SalesOrderRecords on quote.SalesOrderId equals order.Id
         join retailer in db.RetailerRecords on order.RetailerId equals retailer.Id
         where quote.Id == quoteId && retailer.UserAccountId == userId
         select quote.Id).AnyAsync(ct);

    public async Task<IReadOnlyCollection<QuoteIssueOrder>> ListOrdersForPlanAsync(Guid planVersionId, CancellationToken ct) =>
        await (from price in db.AllocationPlanPriceRecords.AsNoTracking()
               join line in db.AllocationRunLineRecords.AsNoTracking() on price.AllocationRunLineId equals line.Id
               join runOrder in db.AllocationRunOrderRecords.AsNoTracking() on line.AllocationRunOrderId equals runOrder.Id
               where price.AllocationPlanVersionId == planVersionId
               group price by new { runOrder.SalesOrderId, runOrder.AllocationRunId } into grouped
               select new QuoteIssueOrder(grouped.Key.SalesOrderId, grouped.Key.AllocationRunId, grouped.Sum(x => x.Amount))).ToArrayAsync(ct);

    public Task<bool> HasPurchaseOrderAsync(Guid allocationPlanVersionId, CancellationToken ct) =>
        db.PurchaseOrderRecords.AnyAsync(x => x.SalesOrderQuoteId != Guid.Empty && db.SalesOrderQuoteRecords.Any(q => q.Id == x.SalesOrderQuoteId && q.AllocationPlanVersionId == allocationPlanVersionId), ct);

    public void Add(SalesOrderQuote quote) => db.SalesOrderQuoteRecords.Add(quote);
    public void AddPurchaseOrder(PurchaseOrder purchaseOrder) => db.PurchaseOrderRecords.Add(purchaseOrder);
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    private IQueryable<QuoteView> Build(IQueryable<SalesOrderQuote> quotes) =>
        from quote in quotes
        join order in db.SalesOrderRecords.AsNoTracking() on quote.SalesOrderId equals order.Id
        join po0 in db.PurchaseOrderRecords.AsNoTracking() on quote.PurchaseOrderId equals po0.Id into pos
        from po in pos.DefaultIfEmpty()
        select new QuoteView(quote, order, po);
}
