using System.Data;
using FSCM.Application.Features.Deliveries;
using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.Deliveries;

public sealed class DeliveryStore(AppDbContext dbContext) : IDeliveryStore
{
    public async Task<IReadOnlyCollection<Driver>> ListDriversAsync(
        bool includeInactive, CancellationToken cancellationToken)
    {
        var query = dbContext.DriverRecords.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(item => item.IsActive);
        return await query.OrderBy(item => item.FullName).ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Vehicle>> ListVehiclesAsync(
        bool includeInactive, CancellationToken cancellationToken)
    {
        var query = dbContext.VehicleRecords.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(item => item.IsActive);
        return await query.OrderBy(item => item.PlateNumber).ToArrayAsync(cancellationToken);
    }

    public Task<Driver?> FindDriverAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.DriverRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<Vehicle?> FindVehicleAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.VehicleRecords.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<bool> WarehouseExistsAsync(Guid warehouseId, CancellationToken cancellationToken) =>
        dbContext.WarehouseRecords.AsNoTracking().AnyAsync(item => item.Id == warehouseId, cancellationToken);

    public Task<bool> DriverCodeExistsAsync(string code, Guid? excludeId, CancellationToken cancellationToken) =>
        dbContext.DriverRecords.AsNoTracking()
            .AnyAsync(item => item.Code == code && (!excludeId.HasValue || item.Id != excludeId.Value), cancellationToken);

    public Task<bool> VehiclePlateExistsAsync(string plateNumber, Guid? excludeId, CancellationToken cancellationToken) =>
        dbContext.VehicleRecords.AsNoTracking()
            .AnyAsync(item => item.PlateNumber == plateNumber && (!excludeId.HasValue || item.Id != excludeId.Value), cancellationToken);

    public async Task<string> NextDriverCodeAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT nextval('\"DriverCodeSequence\"');";
            var number = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return $"DRV-{number:00}";
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    public Task<Driver?> FindDriverForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.DriverRecords.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<Vehicle?> FindVehicleForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.VehicleRecords.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public void AddDriver(Driver driver) => dbContext.DriverRecords.Add(driver);
    public void AddVehicle(Vehicle vehicle) => dbContext.VehicleRecords.Add(vehicle);

    public async Task<DeliveryOrderView?> FindOrderAsync(
        Guid orderId, bool forUpdate, CancellationToken cancellationToken)
    {
        var query = dbContext.SalesOrderRecords.Where(item => item.Id == orderId);
        if (!forUpdate) query = query.AsNoTracking();
        var order = await query.SingleOrDefaultAsync(cancellationToken);
        if (order is null) return null;
        var retailer = await dbContext.RetailerRecords.AsNoTracking()
            .SingleAsync(item => item.Id == order.RetailerId, cancellationToken);
        return new(order, retailer);
    }

    public async Task<IReadOnlyCollection<DeliveryLabelView>> ListOrderLabelsAsync(
        Guid orderId, bool forUpdate, CancellationToken cancellationToken)
    {
        var labelIds = await (
            from list in dbContext.PickingListRecords.AsNoTracking()
            join labelEvent in dbContext.QrLabelEventRecords.AsNoTracking()
                on list.PickingCode equals labelEvent.ReferenceCode
            where list.SalesOrderId == orderId && labelEvent.EventType == QrLabelEventTypes.Picked
            select new { labelEvent.BatchQrLabelId, list.PickingCode })
            .Distinct().ToArrayAsync(cancellationToken);
        var ids = labelIds.Select(item => item.BatchQrLabelId).ToArray();
        var labelQuery = dbContext.BatchQrLabelRecords.Where(item => ids.Contains(item.Id));
        if (!forUpdate) labelQuery = labelQuery.AsNoTracking();
        var labels = await labelQuery.OrderBy(item => item.QrCode).ToArrayAsync(cancellationToken);
        var pickingCodes = labelIds.ToDictionary(item => item.BatchQrLabelId, item => item.PickingCode);
        return labels.Select(label => new DeliveryLabelView(
            label, label.BatchId, label.QrCode, pickingCodes[label.Id])).ToArray();
    }

    public async Task<IReadOnlyCollection<DeliveryInventoryIssue>> ListOrderInventoryIssuesAsync(
        Guid orderId, CancellationToken cancellationToken) =>
        await (
            from list in dbContext.PickingListRecords.AsNoTracking()
            join item in dbContext.PickingItemRecords.AsNoTracking() on list.Id equals item.PickingListId
            join allocation in dbContext.AllocationRecords.AsNoTracking() on item.AllocationId equals allocation.Id
            join balance in dbContext.InventoryBalanceRecords.AsNoTracking() on allocation.BatchId equals balance.BatchId
            where list.SalesOrderId == orderId
            orderby allocation.BatchId, allocation.AllocationSequence, allocation.Id
            select new DeliveryInventoryIssue(allocation.Id, allocation.BatchId, balance.Version))
            .ToArrayAsync(cancellationToken);

    public async Task<DeliveryView?> FindByOrderAsync(
        Guid orderId, bool forUpdate, CancellationToken cancellationToken)
    {
        var deliveryQuery = dbContext.DeliveryRecords.Where(item => item.SalesOrderId == orderId);
        if (!forUpdate) deliveryQuery = deliveryQuery.AsNoTracking();
        var delivery = await deliveryQuery.SingleOrDefaultAsync(cancellationToken);
        return delivery is null ? null : await LoadAsync(delivery, forUpdate, cancellationToken);
    }

    public async Task<DeliveryView?> FindByOperationKeyAsync(
        string operationKey, CancellationToken cancellationToken)
    {
        var orderId = await dbContext.DeliveryRecords.AsNoTracking()
            .Where(item => item.OperationKey == operationKey)
            .Select(item => (Guid?)item.SalesOrderId).SingleOrDefaultAsync(cancellationToken);
        return orderId.HasValue ? await FindByOrderAsync(orderId.Value, false, cancellationToken) : null;
    }

    public async Task<DeliveryView?> FindByReceiptOperationKeyAsync(
        string operationKey, CancellationToken cancellationToken)
    {
        var orderId = await dbContext.ReceiptConfirmationRecords.AsNoTracking()
            .Where(item => item.OperationKey == operationKey && item.ShipmentId == null)
            .Select(item => (Guid?)item.SalesOrderId).SingleOrDefaultAsync(cancellationToken);
        return orderId.HasValue ? await FindByOrderAsync(orderId.Value, false, cancellationToken) : null;
    }

    public async Task<IReadOnlyCollection<DeliveryView>> ListAsync(
        Guid actorUserId,
        string actorRole,
        string? status,
        CancellationToken cancellationToken)
    {
        var query = dbContext.DeliveryRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(item => item.Status == status);
        query = actorRole switch
        {
            SystemRoles.Admin or SystemRoles.Operator => query,
            SystemRoles.Sales => query.Where(item => dbContext.SalesOrderRecords.Any(order =>
                order.Id == item.SalesOrderId && order.CreatedByUserId == actorUserId)),
            SystemRoles.SalesManager => query.Where(item => dbContext.SalesOrderRecords.Any(order =>
                order.Id == item.SalesOrderId && dbContext.SalesGroupMemberRecords.Any(membership =>
                    membership.UserAccountId == actorUserId && membership.SalesGroupId == order.SalesGroupId &&
                    membership.IsActive))),
            // See the note in ShipmentStore: both allocation tables, or a keeper sees no delivery
            // for any order the run produced.
            SystemRoles.WarehouseKeeper => query.Where(item => dbContext.SalesOrderRecords.Any(order =>
                order.Id == item.SalesOrderId && dbContext.SalesOrderLineRecords.Any(line =>
                    line.SalesOrderId == order.Id &&
                    (dbContext.AllocationRecords.Any(allocation =>
                            allocation.SalesOrderLineId == line.Id &&
                            dbContext.UserWarehouseAssignmentRecords.Any(assignment =>
                                assignment.UserAccountId == actorUserId && assignment.IsActive &&
                                assignment.WarehouseId == allocation.WarehouseId)) ||
                     dbContext.AllocationRunAllocationRecords.Any(allocation =>
                            dbContext.AllocationRunLineRecords.Any(runLine =>
                                runLine.Id == allocation.AllocationRunLineId &&
                                runLine.SalesOrderLineId == line.Id) &&
                            dbContext.UserWarehouseAssignmentRecords.Any(assignment =>
                                assignment.UserAccountId == actorUserId && assignment.IsActive &&
                                assignment.WarehouseId == allocation.WarehouseId)))))),
            SystemRoles.Retailer => query.Where(item => dbContext.SalesOrderRecords.Any(order =>
                order.Id == item.SalesOrderId && dbContext.RetailerRecords.Any(retailer =>
                    retailer.Id == order.RetailerId && retailer.UserAccountId == actorUserId))),
            _ => query.Where(_ => false)
        };
        var orderIds = await query.OrderByDescending(item => item.AssignedAt)
            .Select(item => item.SalesOrderId).ToArrayAsync(cancellationToken);
        var results = new List<DeliveryView>(orderIds.Length);
        foreach (var orderId in orderIds)
        {
            var view = await FindByOrderAsync(orderId, false, cancellationToken);
            if (view is not null) results.Add(view);
        }
        return results;
    }

    public Task<bool> CanViewAsync(
        DeliveryView delivery,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken) => actorRole switch
        {
            SystemRoles.Admin or SystemRoles.Operator => Task.FromResult(true),
            SystemRoles.Sales => Task.FromResult(delivery.Order.CreatedByUserId == actorUserId),
            SystemRoles.SalesManager => dbContext.SalesGroupMemberRecords.AsNoTracking().AnyAsync(item =>
                item.UserAccountId == actorUserId && item.SalesGroupId == delivery.Order.SalesGroupId &&
                item.IsActive, cancellationToken),
            SystemRoles.WarehouseKeeper => dbContext.UserWarehouseAssignmentRecords.AsNoTracking().AnyAsync(item =>
                item.UserAccountId == actorUserId && item.IsActive &&
                dbContext.AllocationRecords.Any(allocation => allocation.WarehouseId == item.WarehouseId &&
                    dbContext.SalesOrderLineRecords.Any(line => line.Id == allocation.SalesOrderLineId &&
                        line.SalesOrderId == delivery.Order.Id)), cancellationToken),
            SystemRoles.Retailer => Task.FromResult(delivery.Retailer.UserAccountId == actorUserId),
            _ => Task.FromResult(false)
        };

    public void AddDelivery(Delivery delivery) => dbContext.DeliveryRecords.Add(delivery);
    public void AddReceipt(ReceiptConfirmation receipt) => dbContext.ReceiptConfirmationRecords.Add(receipt);
    public void AddReceiptItem(ReceiptConfirmationItem item) => dbContext.ReceiptConfirmationItemRecords.Add(item);
    public void AddLabelEvent(QrLabelEvent labelEvent) => dbContext.QrLabelEventRecords.Add(labelEvent);
    public void AddOrderHistory(OrderStatusHistory history) => dbContext.OrderStatusHistoryRecords.Add(history);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DeliveryOperationException.Conflict(
                "Order, delivery, or QR label changed concurrently. Refresh and retry.");
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw DeliveryOperationException.Conflict(
                "Delivery operation conflicts with an existing business record.");
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

    private async Task<DeliveryView> LoadAsync(
        Delivery delivery, bool forUpdate, CancellationToken cancellationToken)
    {
        var orderQuery = dbContext.SalesOrderRecords.Where(item => item.Id == delivery.SalesOrderId);
        if (!forUpdate) orderQuery = orderQuery.AsNoTracking();
        var order = await orderQuery.SingleAsync(cancellationToken);
        var retailer = await dbContext.RetailerRecords.AsNoTracking()
            .SingleAsync(item => item.Id == order.RetailerId, cancellationToken);
        var receiptQuery = dbContext.ReceiptConfirmationRecords
            .Where(item => item.SalesOrderId == order.Id && item.ShipmentId == null);
        if (!forUpdate) receiptQuery = receiptQuery.AsNoTracking();
        var receipt = await receiptQuery.SingleOrDefaultAsync(cancellationToken);
        if (receipt is null) return new(delivery, order, retailer, null, []);

        var itemQuery = dbContext.ReceiptConfirmationItemRecords
            .Where(item => item.ReceiptConfirmationId == receipt.Id);
        if (!forUpdate) itemQuery = itemQuery.AsNoTracking();
        var items = await itemQuery.OrderBy(item => item.ScannedAt).ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var labelIds = items.Select(item => item.BatchQrLabelId).ToArray();
        var codes = await dbContext.BatchQrLabelRecords.AsNoTracking()
            .Where(item => labelIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.QrCode, cancellationToken);
        return new(delivery, order, retailer, receipt,
            items.Select(item => new ReceiptItemView(item, codes[item.BatchQrLabelId])).ToArray());
    }
}
