using System.Data;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.Shipments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.Shipments;

public sealed class ShipmentStore(AppDbContext dbContext) : IShipmentStore
{
    public Task<bool> CanAccessWarehouseAsync(Guid userId, string role, Guid warehouseId, CancellationToken cancellationToken) =>
        role is SystemRoles.Admin or SystemRoles.Operator
            ? Task.FromResult(true)
            : dbContext.UserWarehouseAssignmentRecords.AsNoTracking().AnyAsync(item =>
                item.UserAccountId == userId && item.WarehouseId == warehouseId && item.IsActive, cancellationToken);

    public Task<bool> CanViewOrderAsync(Guid orderId, Guid userId, string role, CancellationToken cancellationToken) =>
        role switch
        {
            SystemRoles.Admin or SystemRoles.Operator => Task.FromResult(true),
            SystemRoles.Sales => dbContext.SalesOrderRecords.AsNoTracking().AnyAsync(order =>
                order.Id == orderId && order.CreatedByUserId == userId, cancellationToken),
            SystemRoles.SalesManager => dbContext.SalesOrderRecords.AsNoTracking().AnyAsync(order =>
                order.Id == orderId && dbContext.SalesGroupMemberRecords.Any(membership =>
                    membership.UserAccountId == userId && membership.SalesGroupId == order.SalesGroupId &&
                    membership.IsActive), cancellationToken),
            SystemRoles.WarehouseKeeper => dbContext.SalesOrderLineRecords.AsNoTracking().AnyAsync(line =>
                line.SalesOrderId == orderId && dbContext.AllocationRecords.Any(allocation =>
                    allocation.SalesOrderLineId == line.Id &&
                    dbContext.UserWarehouseAssignmentRecords.Any(assignment =>
                        assignment.UserAccountId == userId && assignment.IsActive &&
                        assignment.WarehouseId == allocation.WarehouseId)), cancellationToken),
            SystemRoles.Retailer => dbContext.SalesOrderRecords.AsNoTracking().AnyAsync(order =>
                order.Id == orderId && dbContext.RetailerRecords.Any(retailer =>
                    retailer.Id == order.RetailerId && retailer.UserAccountId == userId), cancellationToken),
            _ => Task.FromResult(false)
        };

    public async Task<IReadOnlyCollection<ShipmentView>> ListAsync(
        Guid userId, string role, Guid? orderId, Guid? warehouseId, string? status, CancellationToken cancellationToken)
    {
        var query = dbContext.ShipmentRecords.AsNoTracking().AsQueryable();
        if (orderId.HasValue) query = query.Where(item => item.SalesOrderId == orderId.Value);
        if (warehouseId.HasValue) query = query.Where(item => item.WarehouseId == warehouseId.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(item => item.Status == status);
        query = ApplyRoleScope(query, userId, role);
        var shipments = await query.OrderByDescending(item => item.CreatedAt).ToArrayAsync(cancellationToken);
        var results = new List<ShipmentView>(shipments.Length);
        foreach (var shipment in shipments)
        {
            var view = await LoadAsync(shipment, cancellationToken);
            if (view is not null) results.Add(view);
        }
        return results;
    }

    public async Task<ShipmentView?> FindByIdAsync(Guid id, bool forUpdate, CancellationToken cancellationToken)
    {
        var query = dbContext.ShipmentRecords.Where(item => item.Id == id);
        if (!forUpdate) query = query.AsNoTracking();
        var shipment = await query.SingleOrDefaultAsync(cancellationToken);
        return shipment is null ? null : await LoadAsync(shipment, cancellationToken);
    }

    public async Task<IReadOnlyCollection<PendingShipmentSource>> ListPendingSourcesAsync(
        Guid? orderId, CancellationToken cancellationToken)
    {
        var lists = dbContext.PickingListRecords.AsNoTracking()
            .Where(list => list.Status == PickingListStatuses.Completed &&
                           !dbContext.ShipmentRecords.Any(shipment => shipment.PickingListId == list.Id));
        if (orderId.HasValue) lists = lists.Where(list => list.SalesOrderId == orderId.Value);
        var pending = await lists.ToArrayAsync(cancellationToken);
        var results = new List<PendingShipmentSource>(pending.Length);
        foreach (var list in pending)
        {
            var total = await dbContext.PickingItemRecords.AsNoTracking()
                .Where(item => item.PickingListId == list.Id)
                .SumAsync(item => item.PickedQuantity, cancellationToken);
            results.Add(new PendingShipmentSource(list, list.SalesOrderId, list.WarehouseId, total));
        }
        return results;
    }

    public async Task<string> NextShipmentCodeAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT nextval('\"ShipmentCodeSequence\"');";
            var number = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return $"SHIP-{number:000000}";
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    public async Task<IReadOnlyCollection<Shipment>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        await dbContext.ShipmentRecords.AsNoTracking()
            .Where(item => item.SalesOrderId == orderId)
            .OrderBy(item => item.ShipmentCode).ToArrayAsync(cancellationToken);

    public Task<Driver?> FindDriverAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.DriverRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<Vehicle?> FindVehicleAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.VehicleRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public void AddShipment(Shipment shipment) => dbContext.ShipmentRecords.Add(shipment);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ShipmentOperationException.Conflict("Shipment changed concurrently. Refresh and retry.");
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw ShipmentOperationException.Conflict("A shipment already exists for this order and warehouse.");
        }
    }

    private IQueryable<Shipment> ApplyRoleScope(IQueryable<Shipment> query, Guid userId, string role) => role switch
    {
        SystemRoles.Admin or SystemRoles.Operator => query,
        SystemRoles.Sales => query.Where(item => dbContext.SalesOrderRecords.Any(order =>
            order.Id == item.SalesOrderId && order.CreatedByUserId == userId)),
        SystemRoles.SalesManager => query.Where(item => dbContext.SalesOrderRecords.Any(order =>
            order.Id == item.SalesOrderId && dbContext.SalesGroupMemberRecords.Any(membership =>
                membership.UserAccountId == userId && membership.SalesGroupId == order.SalesGroupId &&
                membership.IsActive))),
        SystemRoles.WarehouseKeeper => query.Where(item => dbContext.UserWarehouseAssignmentRecords.Any(assignment =>
            assignment.UserAccountId == userId && assignment.IsActive &&
            assignment.WarehouseId == item.WarehouseId)),
        SystemRoles.Retailer => query.Where(item => dbContext.SalesOrderRecords.Any(order =>
            order.Id == item.SalesOrderId && dbContext.RetailerRecords.Any(retailer =>
                retailer.Id == order.RetailerId && retailer.UserAccountId == userId))),
        _ => query.Where(_ => false)
    };

    private async Task<ShipmentView?> LoadAsync(Shipment shipment, CancellationToken cancellationToken)
    {
        var order = await dbContext.SalesOrderRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == shipment.SalesOrderId, cancellationToken);
        if (order is null) return null;
        var warehouse = await dbContext.WarehouseRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == shipment.WarehouseId, cancellationToken);
        if (warehouse is null) return null;
        var pickingList = await dbContext.PickingListRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == shipment.PickingListId, cancellationToken);
        if (pickingList is null) return null;

        var lines = await (
            from item in dbContext.PickingItemRecords.AsNoTracking()
            where item.PickingListId == shipment.PickingListId
            join allocation in dbContext.AllocationRecords.AsNoTracking() on item.AllocationId equals allocation.Id
            join line in dbContext.SalesOrderLineRecords.AsNoTracking() on allocation.SalesOrderLineId equals line.Id
            join batch in dbContext.BatchRecords.AsNoTracking() on allocation.BatchId equals batch.Id
            orderby line.LineNumber, allocation.AllocationSequence
            select new ShipmentLineView(
                line.Id, line.LineNumber, line.ProductId, line.SkuSnapshot, line.ProductNameSnapshot,
                batch.Id, batch.BatchCode, item.PickedQuantity, allocation.UnitPrice, allocation.NetAmount))
            .ToArrayAsync(cancellationToken);

        return new ShipmentView(shipment, order, warehouse, pickingList, lines);
    }
}
