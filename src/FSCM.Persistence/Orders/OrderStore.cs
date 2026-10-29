using System.Data;
using System.Text.Json;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.AllocationRuns;
using FSCM.Application.Features.WarehouseConfiguration;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.Orders;

public sealed class OrderStore(AppDbContext dbContext) : IOrderStore
{
    public Task<SalesOrder?> FindByClientOrderKeyAsync(Guid clientOrderKey, CancellationToken cancellationToken) =>
        dbContext.SalesOrderRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ClientOrderKey == clientOrderKey, cancellationToken);

    public async Task<OrderRetailerContext?> FindRetailerContextAsync(
        Guid salesUserId, Guid retailerId, CancellationToken cancellationToken)
    {
        var match = await (
            from retailer in dbContext.RetailerRecords.AsNoTracking()
            join assignment in dbContext.RetailerSalesAssignmentRecords.AsNoTracking()
                on retailer.Id equals assignment.RetailerId
            join membership in dbContext.SalesGroupMemberRecords.AsNoTracking()
                on assignment.SalesGroupId equals membership.SalesGroupId
            where retailer.Id == retailerId && retailer.Status == "Active" && assignment.IsActive &&
                  membership.IsActive && membership.UserAccountId == salesUserId
            select new { Retailer = retailer, assignment.SalesGroupId })
            .SingleOrDefaultAsync(cancellationToken);
        return match is null ? null : new(match.Retailer, match.SalesGroupId);
    }

    public Task<Product?> FindPublishedProductAsync(Guid productId, CancellationToken cancellationToken) =>
        dbContext.ProductRecords.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == productId && item.Status == ProductStatuses.Published, cancellationToken);

    public async Task<IReadOnlyCollection<OrderInventoryCandidate>> ListCandidatesAsync(
        Guid productId, CancellationToken cancellationToken) =>
        await (
            from batch in dbContext.BatchRecords.AsNoTracking()
            join balance in dbContext.InventoryBalanceRecords.AsNoTracking() on batch.Id equals balance.BatchId
            join warehouse in dbContext.WarehouseRecords.AsNoTracking() on batch.WarehouseId equals warehouse.Id
            join location in dbContext.MapLocationRecords.AsNoTracking().Where(item => item.WarehouseId.HasValue)
                on warehouse.Id equals location.WarehouseId into locations
            from location in locations.DefaultIfEmpty()
            where batch.ProductId == productId && warehouse.IsActive
            select new OrderInventoryCandidate(
                batch,
                balance,
                warehouse,
                location == null ? null : location.Latitude,
                location == null ? null : location.Longitude))
            .ToArrayAsync(cancellationToken);

    public Task<MapLocation?> FindRetailerLocationAsync(Guid retailerId, CancellationToken cancellationToken) =>
        dbContext.MapLocationRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.RetailerId == retailerId, cancellationToken);

    public async Task<OrderMlorSnapshot?> ResolveMlorAsync(
        Guid retailerId, Guid productId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var product = await dbContext.ProductRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null) return null;

        var active = await dbContext.RetailerProductMlorRecords.AsNoTracking()
            .Where(item => item.RetailerId == retailerId && item.IsActive &&
                           item.EffectiveFrom <= at &&
                           (!item.EffectiveTo.HasValue || item.EffectiveTo.Value > at))
            .ToArrayAsync(cancellationToken);
        var skuRule = active.Where(item => item.ProductId == productId)
            .OrderByDescending(item => item.Version).FirstOrDefault();
        if (skuRule is not null) return Snapshot("Sku", skuRule, null, at);

        var categories = await dbContext.ProductCategoryRecords.AsNoTracking()
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var categoryId = product.ProductCategoryId;
        var visited = new HashSet<Guid>();
        while (visited.Add(categoryId))
        {
            var categoryRule = active.Where(item => item.ProductCategoryId == categoryId)
                .OrderByDescending(item => item.Version).FirstOrDefault();
            if (categoryRule is not null) return Snapshot("Category", categoryRule, categoryId, at);
            if (!categories.TryGetValue(categoryId, out var category) || !category.ParentCategoryId.HasValue)
                break;
            categoryId = category.ParentCategoryId.Value;
        }

        var configuration = await dbContext.SystemConfigurationRecords.AsNoTracking()
            .Where(item => item.ConfigKey == BusinessConfigurationKeys.DefaultMlorRatio &&
                           item.IsActive && item.EffectiveFrom <= at)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (configuration is null) return null;
        using var document = JsonDocument.Parse(configuration.ConfigValue);
        if (!document.RootElement.TryGetDecimal(out var ratio) || ratio <= 0 || ratio >= 1)
            return null;
        return new("Default", null, configuration.Version, null, ratio, null, at);
    }

    private static OrderMlorSnapshot Snapshot(
        string scope, RetailerProductMlor rule, Guid? categoryId, DateTimeOffset at) =>
        new(scope, rule.Id, rule.Version, categoryId, rule.MinimumLifeRatio,
            rule.MinimumLifeHours, at);

    public Task<string?> FindConfigurationAsync(string key, CancellationToken cancellationToken) =>
        dbContext.SystemConfigurationRecords.AsNoTracking()
            .Where(item => item.ConfigKey == key && item.IsActive)
            .OrderByDescending(item => item.Version)
            .Select(item => item.ConfigValue)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<string> NextOrderCodeAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT nextval('\"SalesOrderCodeSequence\"');";
            var number = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            if (number > 999_999) throw OrderOperationException.Conflict("Order code range is exhausted.");
            return $"DH-{number:000000}";
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    public Task<OrderView?> FindAsync(Guid orderId, bool forUpdate, CancellationToken cancellationToken) =>
        LoadAsync(dbContext.SalesOrderRecords.Where(item => item.Id == orderId), forUpdate, cancellationToken);

    public Task<OrderView?> FindByCodeAsync(
        string orderCode, bool forUpdate, CancellationToken cancellationToken) =>
        LoadAsync(dbContext.SalesOrderRecords.Where(item => item.OrderCode == orderCode), forUpdate, cancellationToken);

    public async Task<(IReadOnlyCollection<OrderView> Items, int TotalCount)> ListAsync(
        Guid actorUserId,
        string actorRole,
        OrderListQuery filter,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SalesOrderRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Status)) query = query.Where(item => item.Status == filter.Status);
        if (filter.RetailerId.HasValue) query = query.Where(item => item.RetailerId == filter.RetailerId);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToUpper();
            query = query.Where(item => item.OrderCode.ToUpper().Contains(search) ||
                item.RetailerCodeSnapshot.ToUpper().Contains(search) ||
                item.RetailerNameSnapshot.ToUpper().Contains(search));
        }
        if (filter.CreatedByMe) query = query.Where(item => item.CreatedByUserId == actorUserId);
        query = actorRole switch
        {
            SystemRoles.Admin or SystemRoles.Operator => query,
            SystemRoles.Sales => query.Where(item => item.CreatedByUserId == actorUserId),
            SystemRoles.SalesManager => query.Where(item =>
                dbContext.SalesGroupMemberRecords.Any(membership => membership.UserAccountId == actorUserId &&
                    membership.SalesGroupId == item.SalesGroupId && membership.IsActive)),
            // See the note in ShipmentStore: the run allocates into its own table, so both have
            // to be consulted or a keeper's order list comes back empty.
            SystemRoles.WarehouseKeeper => query.Where(item =>
                dbContext.AllocationRecords.Any(allocation =>
                    dbContext.SalesOrderLineRecords.Any(line => line.Id == allocation.SalesOrderLineId &&
                        line.SalesOrderId == item.Id) &&
                    dbContext.UserWarehouseAssignmentRecords.Any(assignment =>
                        assignment.UserAccountId == actorUserId && assignment.WarehouseId == allocation.WarehouseId &&
                        assignment.IsActive)) ||
                dbContext.AllocationRunAllocationRecords.Any(allocation =>
                    dbContext.AllocationRunLineRecords.Any(runLine =>
                        runLine.Id == allocation.AllocationRunLineId &&
                        dbContext.SalesOrderLineRecords.Any(line => line.Id == runLine.SalesOrderLineId &&
                            line.SalesOrderId == item.Id)) &&
                    dbContext.UserWarehouseAssignmentRecords.Any(assignment =>
                        assignment.UserAccountId == actorUserId && assignment.WarehouseId == allocation.WarehouseId &&
                        assignment.IsActive))),
            SystemRoles.Retailer => query.Where(item =>
                dbContext.RetailerRecords.Any(retailer => retailer.Id == item.RetailerId &&
                    retailer.UserAccountId == actorUserId)),
            _ => query.Where(_ => false)
        };
        var totalCount = await query.CountAsync(cancellationToken);
        var ids = await query.OrderByDescending(item => item.ServerReceivedAt)
            .ThenBy(item => item.OrderCode)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(item => item.Id).ToArrayAsync(cancellationToken);
        var results = new List<OrderView>(ids.Length);
        foreach (var id in ids)
        {
            var view = await FindAsync(id, false, cancellationToken);
            if (view is not null) results.Add(view);
        }

        return (results, totalCount);
    }

    public Task<bool> CanViewAsync(
        OrderView order, Guid actorUserId, string actorRole, CancellationToken cancellationToken) =>
        actorRole switch
        {
            SystemRoles.Admin or SystemRoles.Operator => Task.FromResult(true),
            SystemRoles.Sales => Task.FromResult(order.Order.CreatedByUserId == actorUserId),
            SystemRoles.SalesManager => dbContext.SalesGroupMemberRecords.AsNoTracking().AnyAsync(item =>
                item.UserAccountId == actorUserId && item.SalesGroupId == order.Order.SalesGroupId && item.IsActive,
                cancellationToken),
            SystemRoles.WarehouseKeeper => dbContext.UserWarehouseAssignmentRecords.AsNoTracking().AnyAsync(item =>
                item.UserAccountId == actorUserId && item.IsActive &&
                order.Lines.SelectMany(line => line.Allocations).Select(allocation => allocation.WarehouseId)
                    .Contains(item.WarehouseId), cancellationToken),
            SystemRoles.Retailer => dbContext.RetailerRecords.AsNoTracking().AnyAsync(item =>
                item.Id == order.Order.RetailerId && item.UserAccountId == actorUserId, cancellationToken),
            _ => Task.FromResult(false)
        };

    public void AddOrder(SalesOrder order) => dbContext.SalesOrderRecords.Add(order);
    public void AddLine(SalesOrderLine line) => dbContext.SalesOrderLineRecords.Add(line);
    public void AddAllocation(Allocation allocation) => dbContext.AllocationRecords.Add(allocation);
    public void AddPromotion(OrderPromotion promotion) => dbContext.OrderPromotionRecords.Add(promotion);
    public void AddHistory(OrderStatusHistory history) => dbContext.OrderStatusHistoryRecords.Add(history);
    public void AddAuditLog(AuditLog auditLog) => dbContext.AuditLogRecords.Add(auditLog);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw OrderOperationException.Conflict("Order or inventory changed concurrently. Refresh and retry.");
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw OrderOperationException.Conflict("Order operation conflicts with an existing business record.");
        }
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await operation(cancellationToken);
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

    private async Task<OrderView?> LoadAsync(
        IQueryable<SalesOrder> orderQuery,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        if (!forUpdate) orderQuery = orderQuery.AsNoTracking();
        var order = await orderQuery.SingleOrDefaultAsync(cancellationToken);
        if (order is null) return null;
        var lineQuery = dbContext.SalesOrderLineRecords.Where(item => item.SalesOrderId == order.Id);
        if (!forUpdate) lineQuery = lineQuery.AsNoTracking();
        var lines = await lineQuery.OrderBy(item => item.LineNumber).ToArrayAsync(cancellationToken);
        var lineIds = lines.Select(item => item.Id).ToArray();
        var allocationQuery = dbContext.AllocationRecords.Where(item => lineIds.Contains(item.SalesOrderLineId));
        if (!forUpdate) allocationQuery = allocationQuery.AsNoTracking();
        var allocations = await allocationQuery.OrderBy(item => item.AllocationSequence).ToArrayAsync(cancellationToken);
        var promotionSnapshots = await dbContext.OrderPromotionRecords.AsNoTracking()
            .Where(item => lineIds.Contains(item.SalesOrderLineId))
            .ToArrayAsync(cancellationToken);
        var batchIds = allocations.Select(item => item.BatchId).Distinct().ToArray();
        var warehouseIds = allocations.Select(item => item.WarehouseId).Distinct().ToArray();
        var batchCodes = await dbContext.BatchRecords.AsNoTracking().Where(item => batchIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.BatchCode, cancellationToken);
        var warehouseCodes = await dbContext.WarehouseRecords.AsNoTracking()
            .Where(item => warehouseIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Code, cancellationToken);
        var inventoryVersions = await dbContext.InventoryBalanceRecords.AsNoTracking()
            .Where(item => batchIds.Contains(item.BatchId))
            .ToDictionaryAsync(item => item.BatchId, item => item.Version, cancellationToken);
        var history = await dbContext.OrderStatusHistoryRecords.AsNoTracking()
            .Where(item => item.SalesOrderId == order.Id)
            .OrderBy(item => item.ChangedAt)
            .ToArrayAsync(cancellationToken);
        var deliveries = await dbContext.DeliveryRecords.AsNoTracking()
            .Where(item => item.SalesOrderId == order.Id)
            .OrderBy(item => item.CreatedAt)
            .ToArrayAsync(cancellationToken);
        var lineViews = lines.Select(line => new OrderLineView(
            line,
            allocations.Where(item => item.SalesOrderLineId == line.Id).ToArray(),
            promotionSnapshots.Where(item => item.SalesOrderLineId == line.Id)
                .OrderByDescending(item => item.IsServerFinal)
                .ThenByDescending(item => item.AppliedAt)
                .ThenBy(item => item.Id)
                .FirstOrDefault())).ToArray();
        var allocationContext = await LoadAllocationContextAsync(order.Id, lineIds, cancellationToken);
        return new OrderView(order, lineViews, history, deliveries, batchCodes, warehouseCodes, inventoryVersions)
        {
            AllocationContext = allocationContext
        };
    }

    private async Task<OrderAllocationReadContext?> LoadAllocationContextAsync(
        Guid orderId,
        IReadOnlyCollection<Guid> lineIds,
        CancellationToken cancellationToken)
    {
        var runOrder = await (
            from item in dbContext.AllocationRunOrderRecords.AsNoTracking()
            where item.SalesOrderId == orderId
            join run in dbContext.AllocationRunRecords.AsNoTracking() on item.AllocationRunId equals run.Id
            orderby run.BusinessDate descending, run.StartedAt descending
            select new { Order = item, Run = run }).FirstOrDefaultAsync(cancellationToken);
        if (runOrder is null) return null;

        var plan = await dbContext.AllocationPlanVersionRecords.AsNoTracking()
            .Where(item => item.AllocationRunId == runOrder.Run.Id &&
                (item.Status == AllocationPlanVersionStatuses.Approved ||
                 item.Status == AllocationPlanVersionStatuses.AwaitingApproval))
            .OrderByDescending(item => item.Status == AllocationPlanVersionStatuses.Approved)
            .ThenByDescending(item => item.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (plan is null) return null;

        var runLines = await dbContext.AllocationRunLineRecords.AsNoTracking()
            .Where(item => item.AllocationRunOrderId == runOrder.Order.Id && lineIds.Contains(item.SalesOrderLineId))
            .ToArrayAsync(cancellationToken);
        var runLineIds = runLines.Select(item => item.Id).ToArray();
        var runLineToOrderLine = runLines.ToDictionary(item => item.Id, item => item.SalesOrderLineId);
        var allocations = await dbContext.AllocationRunAllocationRecords.AsNoTracking()
            .Where(item => item.AllocationPlanVersionId == plan.Id && runLineIds.Contains(item.AllocationRunLineId))
            .OrderBy(item => item.Sequence).ToArrayAsync(cancellationToken);
        var prices = await dbContext.AllocationPlanPriceRecords.AsNoTracking()
            .Where(item => item.AllocationPlanVersionId == plan.Id && runLineIds.Contains(item.AllocationRunLineId))
            .ToArrayAsync(cancellationToken);
        var priceByPart = prices.ToDictionary(item => (item.AllocationRunLineId, item.BatchId, item.WarehouseId));

        var batchIds = allocations.Select(item => item.BatchId).Distinct().ToArray();
        var warehouseIds = allocations.Select(item => item.WarehouseId).Distinct().ToArray();
        var batches = await dbContext.BatchRecords.AsNoTracking().Where(item => batchIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var warehouses = await dbContext.WarehouseRecords.AsNoTracking().Where(item => warehouseIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var tierIds = prices.Where(item => item.ShelfLifeDiscountTierId.HasValue)
            .Select(item => item.ShelfLifeDiscountTierId!.Value).Distinct().ToArray();
        var tiers = await dbContext.ShelfLifeDiscountTierRecords.AsNoTracking()
            .Where(item => tierIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var productIds = tiers.Values.Where(item => item.ProductId.HasValue).Select(item => item.ProductId!.Value).Distinct().ToArray();
        var categoryIds = tiers.Values.Where(item => item.ProductCategoryId.HasValue).Select(item => item.ProductCategoryId!.Value).Distinct().ToArray();
        var productCodes = await dbContext.ProductRecords.AsNoTracking().Where(item => productIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Sku, cancellationToken);
        var categoryCodes = await dbContext.ProductCategoryRecords.AsNoTracking().Where(item => categoryIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Code, cancellationToken);

        var fragments = allocations.Select(allocation =>
        {
            if (!priceByPart.TryGetValue((allocation.AllocationRunLineId, allocation.BatchId, allocation.WarehouseId), out var price))
                throw OrderOperationException.Conflict("Allocation price snapshot is incomplete for this order.");
            tiers.TryGetValue(price.ShelfLifeDiscountTierId ?? Guid.Empty, out var tier);
            var ruleCode = tier?.ProductId is Guid productId ? productCodes.GetValueOrDefault(productId)
                : tier?.ProductCategoryId is Guid categoryId ? categoryCodes.GetValueOrDefault(categoryId) : null;
            return new OrderAllocationFragmentView(
                allocation.Id, price.Id, runLineToOrderLine[allocation.AllocationRunLineId],
                allocation.BatchId, batches[allocation.BatchId].BatchCode, batches[allocation.BatchId].ExpirationDate,
                allocation.WarehouseId, warehouses[allocation.WarehouseId].Code,
                allocation.Quantity, allocation.Sequence, allocation.EstimatedArrivalAt,
                allocation.RemainingShelfLifeHours, price.ListUnitPrice, price.BaseNetUnitPrice,
                price.RemainingShelfLifePercent, price.ShelfLifeDiscountTierId, tier?.Scope, ruleCode,
                price.DiscountTableVersion, price.DiscountType, price.DiscountValue,
                price.ShelfLifeDiscountPerUnit, price.FinalNetUnitPrice, price.Amount, price.Explanation);
        }).ToArray();

        var pickingLists = await dbContext.RunPickingListRecords.AsNoTracking()
            .Where(item => item.AllocationPlanVersionId == plan.Id && warehouseIds.Contains(item.WarehouseId))
            .ToArrayAsync(cancellationToken);
        var pickingIds = pickingLists.Select(item => item.Id).ToArray();
        var splits = await (
            from split in dbContext.RunPickingSplitRecords.AsNoTracking()
            where split.SalesOrderId == orderId
            join gather in dbContext.RunPickingGatherRecords.AsNoTracking() on split.RunPickingGatherId equals gather.Id
            where pickingIds.Contains(gather.RunPickingListId)
            select new { gather.RunPickingListId, split.PickedQuantity }).ToArrayAsync(cancellationToken);
        var shipments = await dbContext.ShipmentRecords.AsNoTracking()
            .Where(item => item.SalesOrderId == orderId && warehouseIds.Contains(item.WarehouseId))
            .ToArrayAsync(cancellationToken);
        var progress = warehouseIds.OrderBy(id => warehouses[id].Code).Select(warehouseId =>
        {
            var picking = pickingLists.Where(item => item.WarehouseId == warehouseId)
                .OrderByDescending(item => item.CreatedAt).FirstOrDefault();
            var shipment = shipments.Where(item => item.WarehouseId == warehouseId)
                .OrderByDescending(item => item.CreatedAt).FirstOrDefault();
            return new OrderWarehouseProgressView(
                warehouseId, warehouses[warehouseId].Code, picking?.Id, picking?.PickingCode, picking?.Status,
                shipment?.Id, shipment?.ShipmentCode, shipment?.Status,
                fragments.Where(item => item.WarehouseId == warehouseId).Sum(item => item.Quantity),
                picking is null ? 0m : splits.Where(item => item.RunPickingListId == picking.Id).Sum(item => item.PickedQuantity),
                shipment?.ReceivedQuantity ?? 0m);
        }).ToArray();

        return new OrderAllocationReadContext(
            runOrder.Run.Id, runOrder.Run.RunCode, runOrder.Run.Status, runOrder.Order.Status,
            runOrder.Order.ShortageReason, plan.Id, plan.VersionNumber, plan.Status, plan.PricedAt,
            fragments, progress);
    }

}
