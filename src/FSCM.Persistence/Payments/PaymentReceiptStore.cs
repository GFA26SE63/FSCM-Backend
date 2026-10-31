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
    public async Task<IReadOnlyCollection<ReceiptPaymentView>> FindPaymentsForUpdateAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => await (from payment in db.PaymentRecords where ids.Contains(payment.Id) join order in db.SalesOrderRecords on payment.SalesOrderId equals order.Id select new ReceiptPaymentView(payment, order)).ToArrayAsync(ct);
    public async Task<decimal> ConfirmedAllocationTotalAsync(Guid id, Guid? excludedReceiptId, CancellationToken ct) => await (from allocation in db.PaymentReceiptAllocationRecords join receipt in db.PaymentReceiptRecords on allocation.PaymentReceiptId equals receipt.Id where allocation.PaymentId == id && receipt.Status == PaymentReceiptStatus.Confirmed && (!excludedReceiptId.HasValue || receipt.Id != excludedReceiptId.Value) select allocation.Amount).SumAsync(x => (decimal?)x, ct) ?? 0;
    public void AddReceipt(PaymentReceipt item) => db.PaymentReceiptRecords.Add(item);
    public void AddAllocations(IEnumerable<PaymentReceiptAllocation> items) => db.PaymentReceiptAllocationRecords.AddRange(items);
    public void AddAuditLog(AuditLog item) => db.AuditLogRecords.Add(item);
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct) { await using var tx = await db.Database.BeginTransactionAsync(ct); try { await operation(ct); await tx.CommitAsync(ct); } catch { await tx.RollbackAsync(ct); throw; } }
    public void ClearTracking() => db.ChangeTracker.Clear();
}
