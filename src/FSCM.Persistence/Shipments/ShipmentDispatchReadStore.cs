using FSCM.Application.Features.Shipments;
using FSCM.Domain.Authorization;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.Shipments;

public sealed class ShipmentDispatchReadStore(AppDbContext db) : IShipmentDispatchReadStore
{
    public async Task<IReadOnlyCollection<DispatchPickingRead>> ListPickingAsync(Guid actor, string role, CancellationToken ct)
    {
        var lists = db.PickingListRecords.AsNoTracking().AsQueryable();
        if (role is not (SystemRoles.Admin or SystemRoles.Operator))
            lists = lists.Where(l => ReportingScopeQueries.ManagedWarehouses(db, actor).Any(w => w.Id == l.WarehouseId));
        var headers = await (from list in lists
            join po in db.PurchaseOrderRecords on list.PurchaseOrderId equals po.Id
            orderby list.CreatedAt descending, list.Id
            select new { list.Id, OrderId = po.SalesOrderId, list.WarehouseId }).ToArrayAsync(ct);
        var ids = headers.Select(h => h.Id).ToArray();
        var items = await (from item in db.PickingItemRecords.AsNoTracking()
            join allocation in db.AllocationRecords on item.AllocationId equals allocation.Id
            join line in db.SalesOrderLineRecords on allocation.SalesOrderLineId equals line.Id
            where ids.Contains(item.PickingListId)
            select new { item.PickingListId, Sku = line.SkuSnapshot, ProductName = line.ProductNameSnapshot, item.RequestedQuantity }).ToArrayAsync(ct);
        var byList = items.ToLookup(i => i.PickingListId);
        return headers.Select(h => new DispatchPickingRead(h.Id, h.OrderId, h.WarehouseId,
            byList[h.Id].Select(i => new DispatchPickingItem(i.Sku, i.ProductName, i.RequestedQuantity)).ToArray())).ToArray();
    }

    public async Task<IReadOnlyCollection<ShipmentReadinessSnapshot>> ReadAsync(Guid[] orderIds, Guid actor, string role, CancellationToken ct)
    {
        var orders = await ReportingScopeQueries.ShipmentOrders(db, actor, role).Where(o => orderIds.Contains(o.Id)).ToArrayAsync(ct);
        var ids = orders.Select(o => o.Id).ToArray();
        if (ids.Length == 0) return [];
        var managed = ReportingScopeQueries.ManagedWarehouses(db, actor).Select(w => w.Id);
        var parts = await (from part in db.ShipmentRecords.AsNoTracking()
            join warehouse in db.WarehouseRecords on part.WarehouseId equals warehouse.Id
            where ids.Contains(part.SalesOrderId) && (role != SystemRoles.WarehouseKeeper || managed.Contains(part.WarehouseId))
            select new { part.SalesOrderId, Part = new ShipmentReadinessPartResult(part.Id, part.ShipmentCode,
                warehouse.Id, warehouse.Code, part.Status, part.ExceptionReason) }).ToArrayAsync(ct);
        var expected = await (from list in db.PickingListRecords.AsNoTracking()
            join po in db.PurchaseOrderRecords on list.PurchaseOrderId equals po.Id
            join warehouse in db.WarehouseRecords on list.WarehouseId equals warehouse.Id
            where ids.Contains(po.SalesOrderId) && (role != SystemRoles.WarehouseKeeper || managed.Contains(list.WarehouseId))
            select new { po.SalesOrderId, list.WarehouseId, WarehouseCode = warehouse.Code, list.PickingCode, list.Status })
            .Distinct().ToArrayAsync(ct);
        var payments = await db.PaymentRecords.AsNoTracking().Where(p => ids.Contains(p.SalesOrderId))
            .Select(p => new { p.SalesOrderId, p.Status }).ToDictionaryAsync(p => p.SalesOrderId, p => p.Status, ct);
        var partsByOrder = parts.ToLookup(p => p.SalesOrderId, p => p.Part);
        var expectedByOrder = expected.ToLookup(p => p.SalesOrderId,
            p => new ExpectedWarehousePart(p.WarehouseId, p.WarehouseCode, p.PickingCode, p.Status));
        return orders.Select(o => new ShipmentReadinessSnapshot(o, partsByOrder[o.Id].ToArray(),
            expectedByOrder[o.Id].ToArray(), payments.GetValueOrDefault(o.Id))).ToArray();
    }
}
