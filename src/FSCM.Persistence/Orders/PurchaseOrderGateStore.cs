using FSCM.Application.Features.Orders;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Orders;

public sealed class PurchaseOrderGateStore(AppDbContext db) : IPurchaseOrderGateStore
{
    public async Task<PurchaseOrderGateView?> LoadForUpdateAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.SalesOrderRecords.SingleOrDefaultAsync(item => item.Id == orderId, ct);
        if (order is null) return null;
        var quote = await db.SalesOrderQuoteRecords
            .Where(item => item.SalesOrderId == orderId)
            .OrderByDescending(item => item.RevisionNumber)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Sales order has no quotation.");
        var payment = await db.PaymentRecords.SingleOrDefaultAsync(item => item.SalesOrderId == orderId, ct);
        var po = await db.PurchaseOrderRecords.SingleOrDefaultAsync(item => item.SalesOrderId == orderId, ct);
        var lineIds = await db.SalesOrderLineRecords.Where(item => item.SalesOrderId == orderId)
            .Select(item => item.Id).ToArrayAsync(ct);
        var allocations = await db.AllocationRecords
            .Where(item => lineIds.Contains(item.SalesOrderLineId))
            .OrderBy(item => item.AllocationSequence).ToArrayAsync(ct);
        var warehouseIds = allocations.Select(item => item.WarehouseId).Distinct().ToArray();
        var warehouseCodes = await db.WarehouseRecords.Where(item => warehouseIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Code, ct);
        return new(order, quote, payment, po, allocations, warehouseCodes);
    }

    public void AddPurchaseOrder(PurchaseOrder item) => db.PurchaseOrderRecords.Add(item);
    public void AddPickingList(PickingList item) => db.PickingListRecords.Add(item);
    public void AddPickingItem(PickingItem item) => db.PickingItemRecords.Add(item);
    public void AddHistory(OrderStatusHistory item) => db.OrderStatusHistoryRecords.Add(item);
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                await operation(ct);
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }
    public void ClearTracking() => db.ChangeTracker.Clear();
}
