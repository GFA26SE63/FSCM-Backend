using FSCM.Application.Features.Inventory;

namespace FSCM.Application.Features.Reports;

/// <summary>Filter of the "Báo cáo kho vận" report. Dates are Vietnam calendar days, both inclusive.</summary>
public sealed record WarehouseLogisticsReportQuery(DateOnly FromDate, DateOnly ToDate, Guid? RegionId = null, int Top = 10)
{
    public const int DefaultTop = 10;
    public const int MaxTop = 50;
    public const int MaxPeriodDays = 367;
}

/// <summary>Best-selling SKU across every warehouse in the filter scope, ranked by sales issues in the period.</summary>
public sealed record WarehouseLogisticsProduct(Guid ProductId, string Sku, string Name, int Rank, decimal SoldQuantity);

/// <summary>
/// One best-selling SKU inside one warehouse. Current stock is split so the four stock buckets add up to on-hand:
/// locked quantity first, the rest by expiry (valid / near expiry / expired). Disposed is the warehouse's total
/// written-off quantity (not limited to the period). Only the four receipt/issue columns follow the filter period.
/// </summary>
public sealed record WarehouseLogisticsProductStock(
    Guid ProductId, string Sku, string Name, int Rank,
    decimal OnHand, decimal Valid, decimal NearExpiry, decimal Expired, decimal Locked, decimal Disposed,
    decimal FactoryReceipt, decimal TransferReceipt, decimal SalesIssue, decimal TransferIssue);

public sealed record WarehouseLogisticsWarehouse(
    Guid Id, string Code, string Name, Guid RegionId, string RegionName,
    int DriverCount, int VehicleCount,
    IReadOnlyCollection<WarehouseLogisticsProductStock> Products);

public sealed record WarehouseLogisticsReport(
    DateOnly FromDate, DateOnly ToDate, Guid? RegionId, int Top,
    IReadOnlyCollection<WarehouseLogisticsProduct> TopProducts,
    IReadOnlyCollection<WarehouseLogisticsWarehouse> Warehouses);

public interface IWarehouseLogisticsReportStore
{
    Task<WarehouseLogisticsReport> ReadAsync(WarehouseLogisticsReportQuery query, DateOnly today, CancellationToken ct);
}

// Raw rows loaded by the store; the builder below turns them into the report so the rules are unit-testable.
public sealed record WarehouseLogisticsWarehouseRow(Guid Id, string Code, string Name, Guid RegionId, string RegionName,
    int DriverCount, int VehicleCount);
public sealed record WarehouseLogisticsProductRow(Guid Id, string Sku, string Name, int NearDateThresholdHours);
public sealed record WarehouseLogisticsBatchRow(Guid WarehouseId, Guid ProductId, DateOnly ExpirationDate,
    decimal OnHand, decimal Locked);
/// <summary>Summed ledger delta per warehouse/SKU/type: period receipts and issues, or all-time sales and write-offs.</summary>
public sealed record WarehouseLogisticsMovementRow(Guid WarehouseId, Guid ProductId, string TransactionType,
    decimal PhysicalQuantityDelta);

public static class WarehouseLogisticsReportBuilder
{
    public static void Validate(WarehouseLogisticsReportQuery query)
    {
        if (query.FromDate == default || query.ToDate == default || query.ToDate < query.FromDate ||
            query.ToDate == DateOnly.MaxValue ||
            query.ToDate.DayNumber - query.FromDate.DayNumber >= WarehouseLogisticsReportQuery.MaxPeriodDays)
            throw ReportOperationException.Validation("Select a valid reporting period of at most 367 days.");
        if (query.Top is < 1 or > WarehouseLogisticsReportQuery.MaxTop)
            throw ReportOperationException.Validation("Top must be between 1 and 50.");
    }

    /// <summary>Same rule as the dashboard: near expiry when expiry is within ceil(threshold hours / 24) days.</summary>
    public static bool IsNearExpiry(DateOnly expiry, int thresholdHours, DateOnly today) =>
        expiry >= today && expiry <= today.AddDays((int)Math.Ceiling(thresholdHours / 24m));

    public static WarehouseLogisticsReport Build(
        WarehouseLogisticsReportQuery query, DateOnly today,
        IReadOnlyCollection<WarehouseLogisticsWarehouseRow> warehouses,
        IReadOnlyCollection<WarehouseLogisticsProductRow> products,
        IReadOnlyCollection<WarehouseLogisticsBatchRow> batches,
        IReadOnlyCollection<WarehouseLogisticsMovementRow> movements,
        IReadOnlyCollection<WarehouseLogisticsMovementRow> lifetime)
    {
        var productById = products.ToDictionary(p => p.Id);
        decimal Sold(IEnumerable<WarehouseLogisticsMovementRow> rows, Guid productId) =>
            -rows.Where(m => m.ProductId == productId && m.TransactionType == InventoryTransactionTypes.SalesIssue)
                .Sum(m => m.PhysicalQuantityDelta);
        // Best sellers: period sales first; when the period has few or no sales the list is still filled by
        // all-time sales, then current stock, so the current-stock columns show even without movements.
        var top = productById.Keys
            .Select(id => new
            {
                ProductId = id,
                Period = Sold(movements, id),
                Lifetime = Sold(lifetime, id),
                OnHand = batches.Where(b => b.ProductId == id).Sum(b => Math.Max(b.OnHand, 0m)),
            })
            .Where(x => x.Period > 0 || x.Lifetime > 0 || x.OnHand > 0)
            .OrderByDescending(x => x.Period).ThenByDescending(x => x.Lifetime).ThenByDescending(x => x.OnHand)
            .ThenBy(x => productById[x.ProductId].Sku, StringComparer.Ordinal)
            .Take(query.Top)
            .Select((x, i) => new WarehouseLogisticsProduct(x.ProductId, productById[x.ProductId].Sku,
                productById[x.ProductId].Name, i + 1, x.Period))
            .ToArray();

        var batchesByKey = batches.ToLookup(b => (b.WarehouseId, b.ProductId));
        var movesByKey = movements.ToLookup(m => (m.WarehouseId, m.ProductId));
        var lifetimeByKey = lifetime.ToLookup(m => (m.WarehouseId, m.ProductId));
        decimal Moved(IEnumerable<WarehouseLogisticsMovementRow> rows, string type) =>
            Math.Abs(rows.Where(m => m.TransactionType == type).Sum(m => m.PhysicalQuantityDelta));

        var result = warehouses.OrderBy(w => w.Code, StringComparer.Ordinal).Select(w =>
        {
            var rows = top.Select(p =>
            {
                var threshold = productById[p.ProductId].NearDateThresholdHours;
                decimal valid = 0, near = 0, expired = 0, locked = 0, onHand = 0;
                foreach (var b in batchesByKey[(w.Id, p.ProductId)].Where(b => b.OnHand > 0))
                {
                    var lockedPart = Math.Min(Math.Max(b.Locked, 0m), b.OnHand);
                    var free = b.OnHand - lockedPart;
                    onHand += b.OnHand;
                    locked += lockedPart;
                    if (b.ExpirationDate < today) expired += free;
                    else if (IsNearExpiry(b.ExpirationDate, threshold, today)) near += free;
                    else valid += free;
                }
                var moves = movesByKey[(w.Id, p.ProductId)].ToArray();
                return new WarehouseLogisticsProductStock(p.ProductId, p.Sku, p.Name, p.Rank,
                    onHand, valid, near, expired, locked,
                    Moved(lifetimeByKey[(w.Id, p.ProductId)], InventoryTransactionTypes.WriteOff),
                    Moved(moves, InventoryTransactionTypes.PurchaseReceipt),
                    Moved(moves, InventoryTransactionTypes.TransferReceipt),
                    Moved(moves, InventoryTransactionTypes.SalesIssue),
                    Moved(moves, InventoryTransactionTypes.TransferDispatch));
            }).ToArray();
            return new WarehouseLogisticsWarehouse(w.Id, w.Code, w.Name, w.RegionId, w.RegionName,
                w.DriverCount, w.VehicleCount, rows);
        }).ToArray();

        return new WarehouseLogisticsReport(query.FromDate, query.ToDate, query.RegionId, query.Top, top, result);
    }
}
