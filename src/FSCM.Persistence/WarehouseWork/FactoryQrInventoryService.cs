using FSCM.Application.Features.Inventory;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.WarehouseWork;

public sealed class FactoryQrInventoryRow
{
    public string CartonCode { get; init; } = "";
    public string PalletCode { get; init; } = "";
    public string ProductionBatchCode { get; init; } = "";
    public Guid StockBatchId { get; init; }
    public string StockBatchCode { get; init; } = "";
    public Guid WarehouseId { get; init; }
    public string WarehouseCode { get; init; } = "";
    public string WarehouseName { get; init; } = "";
    public string Sku { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string Status { get; init; } = "";
    public DateOnly ProductionDate { get; init; }
    public DateOnly ExpirationDate { get; init; }
    public int UnitsPerCarton { get; init; }
    public string? ReceiptCode { get; init; }
    public DateTimeOffset? ReceivedAt { get; init; }
    public DateTimeOffset? SyncedAt { get; init; }
    public bool HasAcknowledgement { get; init; }
    public int SyncAttempts { get; init; }
    public string? SyncError { get; init; }
}

public sealed record FactoryQrInventoryPage(FactoryQrInventoryRow[] Items, int TotalCount, int Page, int PageSize);

/// <summary>Read the WMS snapshot and current QR location, even while the factory API is unavailable.</summary>
public sealed class FactoryQrInventoryService(AppDbContext db)
{
    public IQueryable<FactoryQrInventoryRow> Query(Guid actor, string role, Guid? warehouseId,
        string? search, string? status, string? syncStatus)
    {
        if (role is not (SystemRoles.Admin or SystemRoles.Operator or SystemRoles.WarehouseKeeper))
            throw InventoryOperationException.Forbidden("Không được xem tồn QR.");
        var labels = db.BatchQrLabelRecords.AsNoTracking().AsQueryable();
        if (role == SystemRoles.WarehouseKeeper)
            labels = labels.Where(l => ReportingScopeQueries.ManagedWarehouses(db, actor).Any(w => w.Id == l.CurrentWarehouseId));
        if (warehouseId.HasValue) labels = labels.Where(l => l.CurrentWarehouseId == warehouseId);
        if (!string.IsNullOrWhiteSpace(status)) labels = labels.Where(l => l.Status == status);
        var query = from label in labels
                    join carton in db.Set<FactoryCarton>().AsNoTracking() on label.QrCode equals carton.Code
                    join pallet in db.Set<FactoryPallet>().AsNoTracking() on carton.PalletId equals pallet.Id
                    join source in db.Set<FactoryProductionBatch>().AsNoTracking() on pallet.ProductionBatchId equals source.Id
                    join batch in db.BatchRecords.AsNoTracking() on label.BatchId equals batch.Id
                    join warehouse in db.WarehouseRecords.AsNoTracking() on label.CurrentWarehouseId equals warehouse.Id
                    join receipt in db.WarehouseDocuments.AsNoTracking() on pallet.ReceiptDocumentId equals (Guid?)receipt.Id into receipts
                    from receipt in receipts.DefaultIfEmpty()
                    join sync in db.Set<FactoryReceiptSync>().AsNoTracking() on pallet.Id equals sync.PalletId into acknowledgements
                    from sync in acknowledgements.DefaultIfEmpty()
                    select new FactoryQrInventoryRow {
                        CartonCode = label.QrCode, PalletCode = pallet.Code, ProductionBatchCode = source.Code,
                        StockBatchId = batch.Id, StockBatchCode = batch.BatchCode,
                        WarehouseId = warehouse.Id, WarehouseCode = warehouse.Code, WarehouseName = warehouse.Name,
                        Sku = source.Sku, ProductName = source.ProductName, Status = label.Status,
                        ProductionDate = batch.ProductionDate, ExpirationDate = batch.ExpirationDate,
                        UnitsPerCarton = source.UnitsPerCarton, ReceiptCode = receipt == null ? null : receipt.Code,
                        ReceivedAt = pallet.ReceivedAt, SyncedAt = sync == null ? null : sync.SyncedAt,
                        HasAcknowledgement = sync != null, SyncAttempts = sync == null ? 0 : sync.Attempts,
                        SyncError = sync == null ? null : sync.LastError
                    };
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            // Treat scanner/search text literally, including SQL LIKE wildcard characters.
            var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(x => EF.Functions.ILike(x.CartonCode, pattern, "\\") ||
                EF.Functions.ILike(x.PalletCode, pattern, "\\") || EF.Functions.ILike(x.ProductionBatchCode, pattern, "\\") ||
                EF.Functions.ILike(x.StockBatchCode, pattern, "\\") || EF.Functions.ILike(x.Sku, pattern, "\\"));
        }
        query = syncStatus switch {
            "Pending" => query.Where(x => x.HasAcknowledgement && x.SyncedAt == null),
            "Synced" => query.Where(x => x.SyncedAt != null),
            "Legacy" => query.Where(x => !x.HasAcknowledgement),
            null or "" => query,
            _ => throw InventoryOperationException.Validation("Trạng thái đồng bộ không hợp lệ.")
        };
        return query;
    }

    public async Task<FactoryQrInventoryPage> ListAsync(Guid actor, string role, Guid? warehouseId,
        string? search, string? status, string? syncStatus, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
            throw InventoryOperationException.Validation("Trang phải từ 1; số dòng mỗi trang từ 1 đến 100.");
        var query = Query(actor, role, warehouseId, search, status, syncStatus);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.ReceivedAt).ThenBy(x => x.CartonCode)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(items, total, page, pageSize);
    }
}
