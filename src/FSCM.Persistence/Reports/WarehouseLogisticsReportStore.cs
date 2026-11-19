using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.InventoryLocks;
using FSCM.Application.Features.Reports;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Reports;

/// <summary>Loads the raw rows of "Báo cáo kho vận"; ranking and stock buckets live in <see cref="WarehouseLogisticsReportBuilder"/>.</summary>
public sealed class WarehouseLogisticsReportStore(AppDbContext db) : IWarehouseLogisticsReportStore
{
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);

    public async Task<WarehouseLogisticsReport> ReadAsync(WarehouseLogisticsReportQuery query, DateOnly today, CancellationToken ct)
    {
        var start = new DateTimeOffset(query.FromDate.ToDateTime(TimeOnly.MinValue), Vietnam).ToUniversalTime();
        var end = new DateTimeOffset(query.ToDate.AddDays(1).ToDateTime(TimeOnly.MinValue), Vietnam).ToUniversalTime();

        var warehouseQuery = db.WarehouseRecords.AsNoTracking().Where(w => w.IsActive);
        if (query.RegionId.HasValue) warehouseQuery = warehouseQuery.Where(w => w.RegionId == query.RegionId.Value);

        var warehouses = await (from w in warehouseQuery
            join r in db.RegionRecords.AsNoTracking() on w.RegionId equals r.Id into regions
            from r in regions.DefaultIfEmpty()
            select new WarehouseLogisticsWarehouseRow(w.Id, w.Code, w.Name, w.RegionId, r != null ? r.Name : "",
                db.DriverRecords.Count(d => d.WarehouseId == w.Id && d.IsActive),
                db.VehicleRecords.Count(v => v.WarehouseId == w.Id && v.IsActive)))
            .ToArrayAsync(ct);
        var warehouseIds = warehouses.Select(w => w.Id).ToArray();

        var movements = await db.InventoryTransactionRecords.AsNoTracking()
            .Where(t => warehouseIds.Contains(t.WarehouseId) && t.OccurredAt >= start && t.OccurredAt < end
                // Explicit equalities: TransactionType is stored as a smallint code through a value converter.
                && (t.TransactionType == InventoryTransactionTypes.PurchaseReceipt
                    || t.TransactionType == InventoryTransactionTypes.TransferReceipt
                    || t.TransactionType == InventoryTransactionTypes.SalesIssue
                    || t.TransactionType == InventoryTransactionTypes.TransferDispatch))
            .GroupBy(t => new { t.WarehouseId, t.ProductId, t.TransactionType })
            .Select(g => new WarehouseLogisticsMovementRow(g.Key.WarehouseId, g.Key.ProductId, g.Key.TransactionType,
                g.Sum(t => t.PhysicalQuantityDelta)))
            .ToArrayAsync(ct);

        // Not limited to the period: sales history fills the best-seller list, write-offs are the "Hủy" column.
        var lifetime = await db.InventoryTransactionRecords.AsNoTracking()
            .Where(t => warehouseIds.Contains(t.WarehouseId)
                && (t.TransactionType == InventoryTransactionTypes.SalesIssue
                    || t.TransactionType == InventoryTransactionTypes.WriteOff))
            .GroupBy(t => new { t.WarehouseId, t.ProductId, t.TransactionType })
            .Select(g => new WarehouseLogisticsMovementRow(g.Key.WarehouseId, g.Key.ProductId, g.Key.TransactionType,
                g.Sum(t => t.PhysicalQuantityDelta)))
            .ToArrayAsync(ct);

        var batches = await (from batch in db.BatchRecords.AsNoTracking()
            join balance in db.InventoryBalanceRecords.AsNoTracking() on batch.Id equals balance.BatchId
            where warehouseIds.Contains(batch.WarehouseId) && balance.PhysicalQuantity > 0
            select new WarehouseLogisticsBatchRow(batch.WarehouseId, batch.ProductId, batch.ExpirationDate,
                balance.PhysicalQuantity,
                db.InventoryLockRecords.Where(l => l.BatchId == batch.Id && l.Status == InventoryLockStatuses.Active)
                    .Sum(l => (decimal?)l.LockedQuantity) ?? 0m))
            .ToArrayAsync(ct);

        var productIds = movements.Select(m => m.ProductId)
            .Concat(lifetime.Select(m => m.ProductId))
            .Concat(batches.Select(b => b.ProductId))
            .Distinct().ToArray();
        var products = await db.ProductRecords.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new WarehouseLogisticsProductRow(p.Id, p.Sku, p.Name, p.NearDateThresholdHours))
            .ToArrayAsync(ct);

        return WarehouseLogisticsReportBuilder.Build(query, today, warehouses, products, batches, movements, lifetime);
    }
}
