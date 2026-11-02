using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.AllocationRuns;

public sealed class RealTimeAllocationStore(AppDbContext db) : IRealTimeAllocationStore
{
    public async Task<RealTimeAllocationContext?> LoadForUpdateAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.SalesOrderRecords.SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null) return null;
        var lines = await db.SalesOrderLineRecords.Where(x => x.SalesOrderId == orderId).OrderBy(x => x.LineNumber).ToArrayAsync(ct);
        var productIds = lines.Select(x => x.ProductId).Distinct().ToArray();
        var positions = await (from batch in db.BatchRecords
            join balance in db.InventoryBalanceRecords on batch.Id equals balance.BatchId
            join warehouse in db.WarehouseRecords on batch.WarehouseId equals warehouse.Id
            join product in db.ProductRecords on batch.ProductId equals product.Id
            join location in db.MapLocationRecords.Where(x => x.WarehouseId != null) on warehouse.Id equals location.WarehouseId into locations
            from location in locations.DefaultIfEmpty()
            where productIds.Contains(batch.ProductId) && warehouse.IsActive
            select new RealTimeInventoryCandidate(batch, balance, warehouse, product,
                location == null ? null : location.Latitude, location == null ? null : location.Longitude)).ToArrayAsync(ct);
        var retailerLocation = await db.MapLocationRecords.AsNoTracking().SingleOrDefaultAsync(x => x.RetailerId == order.RetailerId, ct);
        var nextAttempt = (await db.AllocationRunRecords.Where(x => x.SalesOrderId == orderId).MaxAsync(x => (int?)x.AttemptNumber, ct) ?? 0) + 1;
        return new(order, lines, retailerLocation, positions, nextAttempt);
    }

    public void AddRun(AllocationRun item) => db.AllocationRunRecords.Add(item);
    public void AddAllocation(Allocation item) => db.AllocationRecords.Add(item);
    public void AddReservation(Reservation item) => db.ReservationRecords.Add(item);
    public void AddTransaction(InventoryTransaction item) => db.InventoryTransactionRecords.Add(item);
    public void AddQuote(SalesOrderQuote item) => db.SalesOrderQuoteRecords.Add(item);
    public void AddHistory(OrderStatusHistory item) => db.OrderStatusHistoryRecords.Add(item);
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new AllocationConcurrencyException("Inventory changed concurrently."); }
    }
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () => { await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct); try { await operation(ct); await tx.CommitAsync(ct); } catch { await tx.RollbackAsync(ct); throw; } });
    }
    public void ClearTracking() => db.ChangeTracker.Clear();
}
