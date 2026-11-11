using FSCM.Application.Features.AllocationRuns;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using FSCM.Application.Features.Orders;

namespace FSCM.Persistence.AllocationRuns;

public sealed class RealTimeAllocationStore(AppDbContext db) : IRealTimeAllocationStore
{
    public async Task<IReadOnlyCollection<(Guid Id, Guid Actor)>> ListRecoverableAsync(DateTimeOffset before, CancellationToken ct) =>
        (await db.SalesOrderRecords.AsNoTracking().Where(o => o.Status == OrderStatuses.Allocating && o.UpdatedAt < before)
            .OrderBy(o => o.UpdatedAt).Take(50).Select(o => new { o.Id, o.CreatedByUserId }).ToArrayAsync(ct))
            .Select(o => (o.Id, o.CreatedByUserId)).ToArray();

    public async Task MarkFailedAsync(Guid orderId, Guid actorId, string status, string reason, DateTimeOffset now, CancellationToken ct)
    {
        await ExecuteInTransactionAsync(async token =>
        {
            db.ChangeTracker.Clear();
            var order = await db.SalesOrderRecords.SingleAsync(o => o.Id == orderId, token);
            if (order.Status is not (OrderStatuses.Allocating or OrderStatuses.AllocationFailed or OrderStatuses.AllocationConflict)) return;
            var old = order.Status;
            order.Status = status; order.RejectionReason = reason; order.UpdatedAt = now; order.Version++;
            var attempt = (await db.AllocationRunRecords.Where(r => r.SalesOrderId == orderId)
                .MaxAsync(r => (int?)r.AttemptNumber, token) ?? 0) + 1;
            db.AllocationRunRecords.Add(new AllocationRun { Id = Guid.NewGuid(), SalesOrderId = orderId,
                RunCode = $"ALT-{order.OrderCode}-{attempt:00}", AttemptNumber = attempt,
                Status = status == OrderStatuses.AllocationConflict ? "Conflict" : "TechnicalFailed",
                FailureReason = reason, TechnicalErrorCode = status, StartedAt = now, CompletedAt = now,
                AlgorithmSettings = "{}", Version = 1, CreatedAt = now, UpdatedAt = now });
            db.OrderStatusHistoryRecords.Add(new() { Id = Guid.NewGuid(), SalesOrderId = orderId,
                FromStatus = old, ToStatus = status, ChangedByUserId = actorId, Reason = reason, ChangedAt = now });
            await SaveChangesAsync(token);
        }, ct);
        ClearTracking();
    }
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
                location == null ? null : location.Latitude, location == null ? null : location.Longitude,
                // F100: quantity held by active inventory locks, excluded from the issuable pool.
                db.InventoryLockRecords.Where(l => l.BatchId == batch.Id && l.Status == "Active")
                    .Sum(l => (decimal?)l.LockedQuantity) ?? 0m)).ToArrayAsync(ct);
        var retailerLocation = await db.MapLocationRecords.AsNoTracking().SingleOrDefaultAsync(x => x.RetailerId == order.RetailerId, ct);
        var nextAttempt = (await db.AllocationRunRecords.Where(x => x.SalesOrderId == orderId).MaxAsync(x => (int?)x.AttemptNumber, ct) ?? 0) + 1;
        return new(order, lines, retailerLocation, positions, nextAttempt);
    }

    public async Task<(string OrderCode, string Status)> ReadOrderStateAsync(
        Guid orderId, CancellationToken ct)
    {
        var state = await db.SalesOrderRecords.AsNoTracking()
            .Where(item => item.Id == orderId)
            .Select(item => new { item.OrderCode, item.Status })
            .SingleAsync(ct);
        return (state.OrderCode, state.Status);
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
        await strategy.ExecuteAsync(async () =>
        {
            // A rolled-back attempt may still be tracked as Quoted with pending inserts. Each
            // execution-strategy replay must read the committed order and stock afresh.
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            try { await operation(ct); await tx.CommitAsync(ct); }
            catch { await tx.RollbackAsync(ct); throw; }
        });
    }
    public void ClearTracking() => db.ChangeTracker.Clear();
}
