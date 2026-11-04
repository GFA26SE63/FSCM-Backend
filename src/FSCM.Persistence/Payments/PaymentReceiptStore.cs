using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Payments;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Payments;

public sealed class PaymentReceiptStore(AppDbContext db) : IPaymentReceiptStore
{
    public Task<PaymentReceipt?> FindReceiptByOperationKeyAsync(string key, bool update, CancellationToken ct) => (update ? db.PaymentReceiptRecords : db.PaymentReceiptRecords.AsNoTracking()).SingleOrDefaultAsync(x => x.OperationKey == key, ct);
    public Task<PaymentReceipt?> FindReceiptAsync(Guid id, bool update, CancellationToken ct) => (update ? db.PaymentReceiptRecords : db.PaymentReceiptRecords.AsNoTracking()).SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyCollection<PaymentReceiptAllocation>> ListAllocationsAsync(Guid id, CancellationToken ct) => await db.PaymentReceiptAllocationRecords.AsNoTracking().Where(x => x.PaymentReceiptId == id).ToArrayAsync(ct);
    public async Task<IReadOnlyCollection<ReceiptPaymentView>> FindPaymentsForUpdateAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var rows = await (from payment in db.PaymentRecords where ids.Contains(payment.Id) join order in db.SalesOrderRecords on payment.SalesOrderId equals order.Id select new { payment, order }).ToArrayAsync(ct);
        var orderIds = rows.Select(x => x.order.Id).ToArray();
        var disputed = await (from d in db.ReceiptDiscrepancyRecords.AsNoTracking() join i in db.ReceiptDiscrepancyItemRecords.AsNoTracking() on d.Id equals i.ReceiptDiscrepancyId where orderIds.Contains(d.SalesOrderId) && d.ResolvedAt == null select new { d.SalesOrderId, i.AdjustmentAmount }).ToArrayAsync(ct);
        return rows.Select(x => new ReceiptPaymentView(x.payment, x.order, disputed.Where(d => d.SalesOrderId == x.order.Id).Sum(d => d.AdjustmentAmount))).ToArray();
    }
    public async Task<decimal> ConfirmedAllocationTotalAsync(Guid id, Guid? excludedReceiptId, CancellationToken ct) => await (from allocation in db.PaymentReceiptAllocationRecords join receipt in db.PaymentReceiptRecords on allocation.PaymentReceiptId equals receipt.Id where allocation.PaymentId == id && receipt.Status == PaymentReceiptStatus.Confirmed && (!excludedReceiptId.HasValue || receipt.Id != excludedReceiptId.Value) select allocation.Amount).SumAsync(x => (decimal?)x, ct) ?? 0;

    public Task<bool> ReferenceInUseAsync(Guid retailerId, string reference, CancellationToken ct) =>
        db.PaymentReceiptRecords.AsNoTracking().AnyAsync(x => x.RetailerId == retailerId
            && x.Reference == reference && x.Status != PaymentReceiptStatus.Reversed, ct);
    public Task<PaymentReceiptAllocation?> FindAllocationForUpdateAsync(Guid receiptId, Guid paymentId, CancellationToken ct) =>
        db.PaymentReceiptAllocationRecords.SingleOrDefaultAsync(x => x.PaymentReceiptId == receiptId && x.PaymentId == paymentId, ct);
    public async Task<IReadOnlyCollection<OutstandingReceivableView>> ListOutstandingReceivablesByRetailerAsync(
        Guid retailerId, DateOnly today, CancellationToken ct)
    {
        // Collectible = AdjustedAmount - ConfirmedAmount (the single source of collected money) - open disputes. Only a
        // receivable with a delivered date and a positive balance is proposable.
        var rows = await (
            from payment in db.PaymentRecords.AsNoTracking()
            join order in db.SalesOrderRecords.AsNoTracking() on payment.SalesOrderId equals order.Id
            where order.RetailerId == retailerId && payment.DeliveredDate != null
                && payment.Method == OrderPaymentMethods.Credit
            let confirmed = payment.ConfirmedAmount
            let disputed = (
                from d in db.ReceiptDiscrepancyRecords
                join i in db.ReceiptDiscrepancyItemRecords on d.Id equals i.ReceiptDiscrepancyId
                where d.SalesOrderId == payment.SalesOrderId && d.ResolvedAt == null
                select (decimal?)i.AdjustmentAmount).Sum() ?? 0m
            select new
            {
                payment.Id,
                payment.SalesOrderId,
                order.OrderCode,
                Outstanding = payment.AdjustedAmount - confirmed - disputed,
                payment.DueDate,
                payment.CreatedAt
            }).ToArrayAsync(ct);

        return rows
            .Where(row => row.Outstanding > 0)
            .Select(row => new OutstandingReceivableView(
                row.Id, row.SalesOrderId, row.OrderCode, row.Outstanding, row.DueDate, row.CreatedAt,
                row.DueDate.HasValue && row.DueDate.Value < today))
            .ToArray();
    }
    public void AddReceipt(PaymentReceipt item) => db.PaymentReceiptRecords.Add(item);
    public void AddAllocations(IEnumerable<PaymentReceiptAllocation> items) => db.PaymentReceiptAllocationRecords.AddRange(items);
    public void AddAuditLog(AuditLog item) => db.AuditLogRecords.Add(item);
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct) { await using var tx = await db.Database.BeginTransactionAsync(ct); try { await operation(ct); await tx.CommitAsync(ct); } catch { await tx.RollbackAsync(ct); throw; } }
    public void ClearTracking() => db.ChangeTracker.Clear();
}


