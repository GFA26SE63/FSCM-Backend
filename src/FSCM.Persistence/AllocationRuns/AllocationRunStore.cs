using System.Text.Json;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.AllocationRuns;

public sealed class AllocationRunStore(AppDbContext dbContext) : IAllocationRunStore
{
    public Task<AllocationRun?> FindByBusinessDateAsync(DateOnly businessDate, CancellationToken cancellationToken) =>
        dbContext.AllocationRunRecords.SingleOrDefaultAsync(item => item.BusinessDate == businessDate, cancellationToken);

    public Task<AllocationRunView?> FindViewAsync(Guid runId, CancellationToken cancellationToken) =>
        BuildViewAsync(dbContext.AllocationRunRecords.AsNoTracking().Where(item => item.Id == runId), cancellationToken);

    public Task<AllocationRunView?> FindViewByBusinessDateAsync(
        DateOnly businessDate, CancellationToken cancellationToken) =>
        BuildViewAsync(
            dbContext.AllocationRunRecords.AsNoTracking().Where(item => item.BusinessDate == businessDate),
            cancellationToken);

    /// <summary>
    /// Orders reviewed and accepted before the cutoff. An order reviewed after the cutoff waits for
    /// the next run even when it was accepted in time, which is what S03 and D7 require.
    /// </summary>
    public async Task<IReadOnlyCollection<EligibleOrderView>> ListEligibleOrdersAsync(
        DateTimeOffset cutoffAt, CancellationToken cancellationToken)
    {
        var orders = await dbContext.SalesOrderRecords.AsNoTracking()
            .Where(item => item.Status == OrderStatuses.PendingAllocation
                && item.ServerReceivedAt < cutoffAt
                && item.ReviewedAt != null && item.ReviewedAt < cutoffAt)
            .ToArrayAsync(cancellationToken);
        if (orders.Length == 0) return [];

        var orderIds = orders.Select(item => item.Id).ToArray();
        var lines = await dbContext.SalesOrderLineRecords.AsNoTracking()
            .Where(item => orderIds.Contains(item.SalesOrderId))
            .Join(dbContext.ProductRecords.AsNoTracking(),
                line => line.ProductId, product => product.Id,
                (line, product) => new { line, product })
            .ToArrayAsync(cancellationToken);

        var linesByOrder = lines.GroupBy(item => item.line.SalesOrderId)
            .ToDictionary(group => group.Key,
                group => (IReadOnlyCollection<EligibleOrderLineView>)group
                    .Select(item => new EligibleOrderLineView(item.line, item.product)).ToArray());

        return orders
            .Where(order => linesByOrder.ContainsKey(order.Id))
            .Select(order => new EligibleOrderView(order, linesByOrder[order.Id]))
            .ToArray();
    }

    /// <summary>
    /// Available stock for the products in demand. Expired batches and batches with nothing
    /// available are left out of the pool rather than filtered later.
    /// </summary>
    public async Task<IReadOnlyCollection<AvailableBatchView>> ListAvailableStockAsync(
        IReadOnlyCollection<Guid> productIds, DateOnly businessDate, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0) return [];
        var ids = productIds.ToArray();
        return await dbContext.BatchRecords.AsNoTracking()
            .Where(batch => ids.Contains(batch.ProductId)
                && batch.ExpiryStatus != BatchExpiryStatuses.Expired
                && batch.ExpirationDate > businessDate)
            .Join(dbContext.InventoryBalanceRecords.AsNoTracking(),
                batch => batch.Id, balance => balance.BatchId,
                (batch, balance) => new { batch, balance })
            .Where(item => item.balance.AvailableQuantity > 0)
            .Select(item => new AvailableBatchView(
                item.batch, item.batch.WarehouseId, item.balance.AvailableQuantity, item.balance.Version))
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Effective MLOR per line: a retailer-SKU ratio wins, then a retailer-Category ratio, then the
    /// configured default. An ordinary promotion never changes it.
    /// </summary>
    public async Task<IReadOnlyDictionary<(Guid RetailerId, Guid ProductId), (decimal Ratio, string Source)>>
        ResolveMlorAsync(
            IReadOnlyCollection<(Guid RetailerId, Guid ProductId)> pairs, CancellationToken cancellationToken)
    {
        var result = new Dictionary<(Guid, Guid), (decimal, string)>();
        if (pairs.Count == 0) return result;

        var retailerIds = pairs.Select(item => item.RetailerId).Distinct().ToArray();
        var productIds = pairs.Select(item => item.ProductId).Distinct().ToArray();
        var categoryByProduct = await dbContext.ProductRecords.AsNoTracking()
            .Where(item => productIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.ProductCategoryId, cancellationToken);

        var configured = await dbContext.RetailerProductMlorRecords.AsNoTracking()
            .Where(item => item.IsActive && retailerIds.Contains(item.RetailerId))
            .ToArrayAsync(cancellationToken);

        var fallback = await ReadDefaultMlorRatioAsync(cancellationToken);
        foreach (var pair in pairs)
        {
            var bySku = configured.FirstOrDefault(
                item => item.RetailerId == pair.RetailerId && item.ProductId == pair.ProductId);
            if (bySku is not null)
            {
                result[pair] = (bySku.MinimumLifeRatio, MlorSources.RetailerProduct);
                continue;
            }

            var categoryId = categoryByProduct.TryGetValue(pair.ProductId, out var category) ? category : Guid.Empty;
            var byCategory = configured.FirstOrDefault(item => item.RetailerId == pair.RetailerId
                && item.ProductId == null
                && item.ProductCategoryId == categoryId);
            result[pair] = byCategory is not null
                ? (byCategory.MinimumLifeRatio, MlorSources.RetailerCategory)
                : (fallback, MlorSources.ProductDefault);
        }

        return result;
    }

    public async Task<IReadOnlyCollection<PlannerBatch>> ListPlannerBatchesAsync(
        Guid runId, CancellationToken cancellationToken) =>
        await dbContext.AllocationRunStockRecords.AsNoTracking()
            .Where(pool => pool.AllocationRunId == runId && pool.RemainingQuantity > 0)
            .Join(dbContext.BatchRecords.AsNoTracking(),
                pool => pool.BatchId, batch => batch.Id, (pool, batch) => new { pool, batch })
            .Join(dbContext.WarehouseRecords.AsNoTracking(),
                item => item.pool.WarehouseId, warehouse => warehouse.Id,
                (item, warehouse) => new PlannerBatch(
                    item.pool.BatchId,
                    item.batch.BatchCode,
                    item.pool.WarehouseId,
                    warehouse.Code,
                    item.pool.ProductId,
                    // A batch is usable up to the end of its expiration day.
                    new DateTimeOffset(item.pool.ExpirationDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero),
                    item.pool.RemainingQuantity))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<(Guid WarehouseId, Guid RetailerId), decimal>> ListDistancesAsync(
        IReadOnlyCollection<Guid> warehouseIds,
        IReadOnlyCollection<Guid> retailerIds,
        CancellationToken cancellationToken)
    {
        if (warehouseIds.Count == 0 || retailerIds.Count == 0)
        {
            return new Dictionary<(Guid, Guid), decimal>();
        }

        var warehouses = warehouseIds.ToArray();
        var retailers = retailerIds.ToArray();
        var rows = await dbContext.MapDistanceCacheRecords.AsNoTracking()
            .Where(item => item.IsActive
                && warehouses.Contains(item.WarehouseId)
                && retailers.Contains(item.RetailerId))
            .Select(item => new { item.WarehouseId, item.RetailerId, item.DistanceMeters })
            .ToArrayAsync(cancellationToken);
        return rows.ToDictionary(
            item => (item.WarehouseId, item.RetailerId), item => (decimal)item.DistanceMeters);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> ListProductCategoriesAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0) return new Dictionary<Guid, Guid>();
        var ids = productIds.ToArray();
        return await dbContext.ProductRecords.AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.ProductCategoryId, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> ListBatchExpiryAsync(
        IReadOnlyCollection<Guid> batchIds, CancellationToken cancellationToken)
    {
        if (batchIds.Count == 0) return new Dictionary<Guid, DateTimeOffset>();
        var ids = batchIds.ToArray();
        var rows = await dbContext.BatchRecords.AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .Select(item => new { item.Id, item.ExpirationDate })
            .ToArrayAsync(cancellationToken);
        return rows.ToDictionary(
            item => item.Id,
            item => new DateTimeOffset(item.ExpirationDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero));
    }

    public void AddAllocations(IEnumerable<AllocationRunAllocation> allocations) =>
        dbContext.AllocationRunAllocationRecords.AddRange(allocations);

    public void AddPlanVersion(AllocationPlanVersion version) => dbContext.AllocationPlanVersionRecords.Add(version);

    public void AddPlanPrices(IEnumerable<AllocationPlanPrice> prices) =>
        dbContext.AllocationPlanPriceRecords.AddRange(prices);

    public async Task<int> MaxPlanVersionNumberAsync(Guid runId, CancellationToken cancellationToken) =>
        await dbContext.AllocationPlanVersionRecords.AsNoTracking()
            .Where(item => item.AllocationRunId == runId)
            .AnyAsync(cancellationToken)
            ? await dbContext.AllocationPlanVersionRecords.AsNoTracking()
                .Where(item => item.AllocationRunId == runId)
                .MaxAsync(item => item.VersionNumber, cancellationToken)
            : 0;

    public Task<AllocationPlanVersion?> FindLivePlanVersionAsync(Guid runId, CancellationToken cancellationToken) =>
        dbContext.AllocationPlanVersionRecords.SingleOrDefaultAsync(
            item => item.AllocationRunId == runId
                && item.Status == AllocationPlanVersionStatuses.AwaitingApproval,
            cancellationToken);

    public Task<AllocationPlanVersion?> FindPlanVersionForUpdateAsync(
        Guid versionId, CancellationToken cancellationToken) =>
        dbContext.AllocationPlanVersionRecords.SingleOrDefaultAsync(item => item.Id == versionId, cancellationToken);

    public Task<AllocationPlanVersionView?> FindPlanVersionViewAsync(
        Guid versionId, CancellationToken cancellationToken) =>
        CreatePlanVersionQuery(dbContext.AllocationPlanVersionRecords.AsNoTracking()
            .Where(item => item.Id == versionId)).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<AllocationPlanVersionView>> ListPlanVersionsAsync(
        Guid runId, CancellationToken cancellationToken) =>
        await CreatePlanVersionQuery(dbContext.AllocationPlanVersionRecords.AsNoTracking()
                .Where(item => item.AllocationRunId == runId)
                .OrderByDescending(item => item.VersionNumber))
            .ToArrayAsync(cancellationToken);

    public Task<AllocationRun?> FindRunForUpdateAsync(Guid runId, CancellationToken cancellationToken) =>
        dbContext.AllocationRunRecords.SingleOrDefaultAsync(item => item.Id == runId, cancellationToken);

    public async Task<IReadOnlyCollection<ReservableAllocation>> ListReservableAsync(
        Guid planVersionId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.AllocationRunAllocationRecords
            .Where(allocation => allocation.AllocationPlanVersionId == planVersionId)
            .Join(dbContext.BatchRecords, allocation => allocation.BatchId, batch => batch.Id,
                (allocation, batch) => new { allocation, batch })
            .Join(dbContext.InventoryBalanceRecords, item => item.batch.Id, balance => balance.BatchId,
                (item, balance) => new { item.allocation, item.batch, balance })
            .ToArrayAsync(cancellationToken);
        if (rows.Length == 0) return [];

        var allocationIds = rows.Select(item => item.allocation.Id).ToArray();
        var held = await dbContext.ReservationRecords.AsNoTracking()
            .Where(item => item.AllocationRunAllocationId != null
                && allocationIds.Contains(item.AllocationRunAllocationId.Value)
                && item.Status == "Active")
            .Select(item => item.AllocationRunAllocationId!.Value)
            .ToArrayAsync(cancellationToken);
        var heldSet = held.ToHashSet();

        return rows
            .Select(item => new ReservableAllocation(
                item.allocation, item.batch, item.balance, heldSet.Contains(item.allocation.Id)))
            .ToArray();
    }

    public void AddReservation(Reservation reservation) => dbContext.ReservationRecords.Add(reservation);

    public void AddInventoryTransaction(InventoryTransaction transaction) =>
        dbContext.InventoryTransactionRecords.Add(transaction);

    private IQueryable<AllocationPlanVersionView> CreatePlanVersionQuery(
        IQueryable<AllocationPlanVersion> versions) =>
        versions.Join(dbContext.AllocationRunRecords.AsNoTracking(),
            version => version.AllocationRunId, run => run.Id,
            (version, run) => new AllocationPlanVersionView(
                version,
                run.RunCode,
                dbContext.AllocationPlanPriceRecords.AsNoTracking()
                    .Where(price => price.AllocationPlanVersionId == version.Id)
                    .ToArray()));

    public async Task<AllocationSettings> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var weights = await ReadConfigurationAsync(BusinessConfigurationKeys.AllocationWeights, cancellationToken);
        var radius = await ReadConfigurationAsync(BusinessConfigurationKeys.ServiceRadiusMeters, cancellationToken);

        var shelfLife = 0.5m;
        var distance = 0.3m;
        var coverage = 0.2m;
        if (weights is not null)
        {
            using var document = JsonDocument.Parse(weights);
            shelfLife = ReadDecimal(document.RootElement, "expiry", shelfLife);
            distance = ReadDecimal(document.RootElement, "distance", distance);
            coverage = ReadDecimal(document.RootElement, "coverage", coverage);
        }

        var servingDistance = 20_000;
        if (radius is not null && int.TryParse(radius, out var parsed) && parsed > 0)
        {
            servingDistance = parsed;
        }

        return new AllocationSettings(shelfLife, distance, coverage, servingDistance);
    }

    public async Task<string> NextRunCodeAsync(DateOnly businessDate, CancellationToken cancellationToken)
    {
        var sequence = await dbContext.AllocationRunRecords.AsNoTracking()
            .CountAsync(item => item.BusinessDate == businessDate, cancellationToken);
        return $"DOT-{businessDate:yyyyMMdd}-{sequence + 1:00}";
    }

    public void AddRun(AllocationRun run) => dbContext.AllocationRunRecords.Add(run);

    public void AddOrders(IEnumerable<AllocationRunOrder> orders) => dbContext.AllocationRunOrderRecords.AddRange(orders);

    public void AddLines(IEnumerable<AllocationRunLine> lines) => dbContext.AllocationRunLineRecords.AddRange(lines);

    public void AddPool(IEnumerable<AllocationRunStock> pool) => dbContext.AllocationRunStockRecords.AddRange(pool);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await action(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public void ClearTracking() => dbContext.ChangeTracker.Clear();

    private async Task<AllocationRunView?> BuildViewAsync(
        IQueryable<AllocationRun> runs, CancellationToken cancellationToken)
    {
        var run = await runs.SingleOrDefaultAsync(cancellationToken);
        if (run is null) return null;

        var orders = await dbContext.AllocationRunOrderRecords.AsNoTracking()
            .Where(item => item.AllocationRunId == run.Id)
            .OrderBy(item => item.PriorityRank)
            .ToArrayAsync(cancellationToken);
        var orderIds = orders.Select(item => item.Id).ToArray();
        var lines = await dbContext.AllocationRunLineRecords.AsNoTracking()
            .Where(item => orderIds.Contains(item.AllocationRunOrderId))
            .ToArrayAsync(cancellationToken);
        var pool = await dbContext.AllocationRunStockRecords.AsNoTracking()
            .Where(item => item.AllocationRunId == run.Id)
            .ToArrayAsync(cancellationToken);

        return new AllocationRunView(run, orders, lines, pool);
    }

    private Task<string?> ReadConfigurationAsync(string key, CancellationToken cancellationToken) =>
        dbContext.SystemConfigurationRecords.AsNoTracking()
            .Where(item => item.ConfigKey == key && item.IsActive)
            .Select(item => (string?)item.ConfigValue)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<decimal> ReadDefaultMlorRatioAsync(CancellationToken cancellationToken)
    {
        var raw = await ReadConfigurationAsync(BusinessConfigurationKeys.DefaultMlorRatio, cancellationToken);
        return raw is not null && decimal.TryParse(raw, out var parsed) && parsed is > 0 and < 1 ? parsed : 0.25m;
    }

    private static decimal ReadDecimal(JsonElement element, string name, decimal fallback) =>
        element.TryGetProperty(name, out var property) && property.TryGetDecimal(out var value) ? value : fallback;
}
