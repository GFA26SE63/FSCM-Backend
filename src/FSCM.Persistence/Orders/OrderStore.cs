using System.Data;
using FSCM.Application.Features.Orders;
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

    public async Task<IReadOnlyCollection<TransitTimeRule>> ListTransitRulesAsync(
        IReadOnlyCollection<Guid> warehouseIds, CancellationToken cancellationToken) =>
        await dbContext.TransitTimeRuleRecords.AsNoTracking()
            .Where(item => warehouseIds.Contains(item.WarehouseId) && item.IsActive)
            .ToArrayAsync(cancellationToken);

    public Task<decimal?> FindMlorRatioAsync(
        Guid retailerId, Guid productId, CancellationToken cancellationToken) =>
        dbContext.RetailerProductMlorRecords.AsNoTracking()
            .Where(item => item.RetailerId == retailerId && item.ProductId == productId && item.IsActive)
            .OrderByDescending(item => item.EffectiveFrom)
            .Select(item => (decimal?)item.MinimumLifeRatio)
            .FirstOrDefaultAsync(cancellationToken);

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

    public async Task<IReadOnlyCollection<OrderView>> ListAsync(
        Guid actorUserId,
        string actorRole,
        string? status,
        Guid? retailerId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SalesOrderRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(item => item.Status == status);
        if (retailerId.HasValue) query = query.Where(item => item.RetailerId == retailerId);
        query = actorRole switch
        {
            SystemRoles.Admin or SystemRoles.Operator => query,
            SystemRoles.Sales => query.Where(item => item.CreatedByUserId == actorUserId),
            SystemRoles.SalesManager => query.Where(item =>
                dbContext.SalesGroupMemberRecords.Any(membership => membership.UserAccountId == actorUserId &&
                    membership.SalesGroupId == item.SalesGroupId && membership.IsActive)),
            SystemRoles.WarehouseKeeper => query.Where(item =>
                dbContext.AllocationRecords.Any(allocation =>
                    dbContext.SalesOrderLineRecords.Any(line => line.Id == allocation.SalesOrderLineId &&
                        line.SalesOrderId == item.Id) &&
                    dbContext.UserWarehouseAssignmentRecords.Any(assignment =>
                        assignment.UserAccountId == actorUserId && assignment.WarehouseId == allocation.WarehouseId &&
                        assignment.IsActive))),
            SystemRoles.Retailer => query.Where(item =>
                dbContext.RetailerRecords.Any(retailer => retailer.Id == item.RetailerId &&
                    retailer.UserAccountId == actorUserId)),
            _ => query.Where(_ => false)
        };
        var ids = await query.OrderByDescending(item => item.ServerReceivedAt)
            .Select(item => item.Id).ToArrayAsync(cancellationToken);
        var results = new List<OrderView>(ids.Length);
        foreach (var id in ids)
        {
            var view = await FindAsync(id, false, cancellationToken);
            if (view is not null) results.Add(view);
        }

        return results;
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
        var lineViews = lines.Select(line => new OrderLineView(
            line, allocations.Where(item => item.SalesOrderLineId == line.Id).ToArray())).ToArray();
        return new OrderView(order, lineViews, history, batchCodes, warehouseCodes, inventoryVersions);
    }

}
