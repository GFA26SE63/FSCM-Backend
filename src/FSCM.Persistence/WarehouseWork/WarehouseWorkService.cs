using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.Picking;
using FSCM.Application.Features.QrLabels;
using FSCM.Application.Features.Shipments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.WarehouseWork;

public sealed record TransferWorkLine(Guid BatchId, int Quantity);
public sealed record CreateWarehouseRequest(Guid WarehouseId, Guid? DestinationWarehouseId,
    string Reason, Guid[] OrderIds, TransferWorkLine[] Lines, string? Note);
public sealed record FactoryReceiptRequest(Guid WarehouseId, ReceiveGoodsLineData[]? Lines = null, string? Note = null);
public sealed record WarehousePalletMapping(string PalletCode, string CartonCode, int UnitsPerCarton);
public sealed record WarehouseWorkResult(WarehouseDocument Document,
    IReadOnlyCollection<WarehouseDocumentLine> Lines, IReadOnlyCollection<WarehouseDocumentCarton> Cartons,
    IReadOnlyCollection<WarehousePalletMapping>? Pallets = null);

/// <summary>Document orchestration shares the allocation/picking stock ledger and QR event history.</summary>
public sealed partial class WarehouseWorkService(AppDbContext db, InventoryService inventory,
    CurrentPickingService picking, ShipmentService shipments, QrLabelService qrLabels, TimeProvider clock, FactoryTraceabilityService factory)
{
    private DbSet<WarehouseDocument> Documents => db.Set<WarehouseDocument>();
    private DbSet<WarehouseDocumentLine> Lines => db.Set<WarehouseDocumentLine>();
    private DbSet<WarehouseDocumentCarton> Cartons => db.Set<WarehouseDocumentCarton>();

    public async Task<WarehouseWorkResult[]> ListAsync(Guid actor, string role, CancellationToken ct)
    {
        var q = Documents.AsNoTracking().AsQueryable();
        if (role == SystemRoles.WarehouseKeeper)
            q = q.Where(x => ReportingScopeQueries.ManagedWarehouses(db, actor).Any(w =>
                w.Id == x.WarehouseId || w.Id == x.DestinationWarehouseId));
        var docs = await q.OrderByDescending(x => x.CreatedAt).ToArrayAsync(ct);
        var ids = docs.Select(x => x.Id).ToArray();
        var lines = await Lines.AsNoTracking().Where(x => ids.Contains(x.DocumentId)).ToArrayAsync(ct);
        var cartons = await Cartons.AsNoTracking().Where(x => ids.Contains(x.DocumentId)).ToArrayAsync(ct);
        var mapping = await PalletMappings(cartons.Select(c => c.CartonId).ToArray(), ct);
        return docs.Select(d => new WarehouseWorkResult(d, lines.Where(x => x.DocumentId == d.Id).ToArray(),
            cartons.Where(x => x.DocumentId == d.Id).ToArray(),
            mapping.Where(m => cartons.Any(c => c.DocumentId == d.Id && c.CartonId == m.CartonCode)).ToArray())).ToArray();
    }

    public async Task<WarehouseWorkResult> GetAsync(Guid id, Guid actor, string role, CancellationToken ct)
    {
        var d = await Find(id, ct);
        if (!await HasAccess(d.WarehouseId, actor, role, ct) &&
            !(d.DestinationWarehouseId.HasValue && await HasAccess(d.DestinationWarehouseId.Value, actor, role, ct)))
            throw PickingOperationException.Forbidden("Phiếu không thuộc kho bạn quản lý.");
        var cartons = await Cartons.AsNoTracking().Where(x => x.DocumentId == id).ToArrayAsync(ct);
        return new(d, await Lines.AsNoTracking().Where(x => x.DocumentId == id).ToArrayAsync(ct),
            cartons, await PalletMappings(cartons.Select(c => c.CartonId).ToArray(), ct));
    }

    public Task<Guid> CreateRequestAsync(CreateWarehouseRequest data, string key, Guid actor, string role, CancellationToken ct) =>
        Atomic(async () =>
        {
            if (role != SystemRoles.Admin) throw PickingOperationException.Forbidden("Chỉ admin được phát hành yêu cầu kho.");
            var replay = await Replay(key, data, actor, ct);
            if (replay.HasValue) return replay.Value;
            await Access(data.WarehouseId, actor, role, ct);
            var d = New("Request", data.Reason, data.WarehouseId, key, data, actor, data.Note);
            if (data.Reason == "Order")
            {
                if (data.DestinationWarehouseId.HasValue || data.OrderIds.Length == 0 ||
                    data.OrderIds.Distinct().Count() != data.OrderIds.Length)
                    throw PickingOperationException.Validation("Chọn các đơn hàng khác nhau, cùng kho xuất.");
                var views = await picking.ListAsync(actor, role, data.WarehouseId, null, ct);
                var selected = views.Where(v => data.OrderIds.Contains(v.OrderId)).ToArray();
                if (selected.Select(v => v.OrderId).Distinct().Count() != data.OrderIds.Length ||
                    selected.Any(v => v.Status != PickingListStatuses.Pending || v.Items.Any(i => i.PickedQuantity != 0)))
                    throw PickingOperationException.Conflict("Đơn phải có allocation/PO và chưa bắt đầu soạn tại kho này.");
                var listIds = selected.Select(x => x.Id).ToArray();
                if (await Lines.AnyAsync(x => listIds.Contains(x.PickingListId ?? Guid.Empty) &&
                    Documents.Any(r => r.Id == x.DocumentId && r.Kind == "Request" && r.Status != "Cancelled"), ct))
                    throw PickingOperationException.Conflict("Một đơn đã thuộc yêu cầu xuất khác tại kho này.");
                foreach (var v in selected)
                    foreach (var item in v.Items)
                    {
                        WholeCartons(item.RequestedQuantity);
                        var batch = await db.BatchRecords.SingleAsync(x => x.Id == item.BatchId, ct);
                        var line = await NewLine(d.Id, batch, item.RequestedQuantity, ct);
                        line.PickingItemId = item.Id; line.PickingListId = v.Id;
                        line.SalesOrderId = v.OrderId; line.OrderCode = v.OrderCode;
                        Lines.Add(line);
                    }
            }
            else if (data.Reason == "Transfer")
            {
                if (!data.DestinationWarehouseId.HasValue || data.DestinationWarehouseId == data.WarehouseId ||
                    data.Lines.Length == 0 || data.Lines.Select(x => x.BatchId).Distinct().Count() != data.Lines.Length)
                    throw PickingOperationException.Validation("Chọn kho nhận khác kho xuất và các batch không trùng.");
                await Access(data.DestinationWarehouseId.Value, actor, role, ct);
                d.DestinationWarehouseId = data.DestinationWarehouseId;
                foreach (var input in data.Lines)
                {
                    WholeCartons(input.Quantity);
                    var batch = await db.BatchRecords.SingleOrDefaultAsync(x => x.Id == input.BatchId, ct)
                        ?? throw PickingOperationException.NotFound("Không tìm thấy batch.");
                    await ValidBatch(batch, data.WarehouseId, ct);
                    var balance = await db.InventoryBalanceRecords.SingleAsync(x => x.BatchId == batch.Id, ct);
                    if (balance.AvailableQuantity < input.Quantity)
                        throw PickingOperationException.Conflict("Không đủ tồn khả dụng để giữ cho luân chuyển.");
                    balance.ReservedQuantity += input.Quantity; balance.AvailableQuantity -= input.Quantity;
                    balance.Version++; balance.UpdatedAt = clock.GetUtcNow();
                    Ledger(d, batch, balance, actor, 0, input.Quantity, "TransferReserve");
                    Lines.Add(await NewLine(d.Id, batch, input.Quantity, ct));
                }
            }
            else throw PickingOperationException.Validation("Lý do yêu cầu phải là Transfer hoặc Order.");
            Documents.Add(d);
            await db.SaveChangesAsync(ct);
            return d.Id;
        }, ct);

    public Task<Guid> CreateChildAsync(Guid sourceId, string key, Guid actor, string role, CancellationToken ct) =>
        Atomic(async () =>
        {
            Keeper(role);
            var replay = await Replay(key, new { sourceId }, actor, ct);
            if (replay.HasValue) return replay.Value;
            var source = await Find(sourceId, ct);
            if (source.Kind == "Request" && source.Status != "Open" ||
                source.Kind == "Issue" && (source.Status != "Posted" || source.Reason != "Transfer") ||
                source.Kind is not ("Request" or "Issue"))
                throw PickingOperationException.Conflict("Chứng từ nguồn không hợp lệ hoặc chưa được xuất.");
            var wh = source.Kind == "Request" ? source.WarehouseId : source.DestinationWarehouseId!.Value;
            await Access(wh, actor, role, ct);
            if (await Documents.AnyAsync(x => x.SourceDocumentId == sourceId, ct))
                throw PickingOperationException.Conflict("Đã có phiếu thực hiện cho chứng từ nguồn này.");
            var d = New(source.Kind == "Request" ? "Issue" : "Receipt", source.Reason, wh,
                key, new { sourceId }, actor, source.Note);
            d.SourceDocumentId = source.Id; d.DestinationWarehouseId = source.DestinationWarehouseId;
            var sourceLines = await Lines.Where(x => x.DocumentId == sourceId).ToArrayAsync(ct);
            foreach (var l in sourceLines)
            {
                var line = JsonSerializer.Deserialize<WarehouseDocumentLine>(JsonSerializer.Serialize(l))!;
                line.Id = Guid.NewGuid(); line.DocumentId = d.Id;
                Lines.Add(line);
                if (d.Kind == "Receipt")
                {
                    foreach (var c in await Cartons.Where(x => x.LineId == l.Id).ToArrayAsync(ct))
                        Cartons.Add(new() { Id = Guid.NewGuid(), DocumentId = d.Id, LineId = line.Id,
                            CartonId = c.CartonId, LabelId = c.LabelId });
                }
            }
            Documents.Add(d);
            await db.SaveChangesAsync(ct);
            return d.Id;
        }, ct);

    public Task<Guid> CreateFactoryAsync(FactoryReceiptRequest data, string key, Guid actor, string role, CancellationToken ct) =>
        Atomic(async () =>
        {
            Keeper(role);
            var replay = await Replay(key, data, actor, ct);
            if (replay.HasValue) return replay.Value;
            await Access(data.WarehouseId, actor, role, ct);
            if (data.Lines is { Length: > 0 })
                throw PickingOperationException.Validation("Nhập từ nhà máy chỉ quét pallet đã phát hành; không nhập SKU/batch hoặc sinh QR tại kho.");
            var d = New("Receipt", "Factory", data.WarehouseId, key, data, actor, data.Note);
            Documents.Add(d);
            await db.SaveChangesAsync(ct);
            return d.Id;
        }, ct);

    public Task<Guid> ScanAsync(Guid id, string cartonId, int version, Guid actor, string role, CancellationToken ct) =>
        Atomic(() => ScanCore(id, cartonId, version, actor, role, ct), ct);

    private async Task<Guid> ScanCore(Guid id, string cartonId, int version, Guid actor, string role, CancellationToken ct, Guid? preferredLineId = null)
        {
            Keeper(role);
            var d = await Find(id, ct);
            await Access(d.WarehouseId, actor, role, ct);
            cartonId = cartonId.Trim();
            if (d.Kind == "Receipt" && d.Reason == "Factory" && d.GoodsReceiptId is null)
                throw PickingOperationException.Conflict("Quét pallet trên phiếu nhập và xác nhận kết quả kiểm đếm trước khi cộng tồn.");
            var existing = await Cartons.SingleOrDefaultAsync(x => x.DocumentId == id && x.CartonId == cartonId, ct);
            // A repeated scanner delivery is an idempotent read, including after another scan changed the version.
            if (existing?.ScannedAt is not null) return id;
            if (d.Kind is not ("Picking" or "Issue" or "Receipt") || d.Status != "Open") throw PickingOperationException.Conflict("Phiếu không nhận quét.");
            Version(d, version);
            if (d.Kind == "Receipt")
            {
                if (existing is null) throw PickingOperationException.Conflict("Carton không thuộc phiếu nhập này.");
                if (d.Reason == "Transfer")
                {
                    var label = await db.BatchQrLabelRecords.SingleAsync(x => x.Id == existing.LabelId, ct);
                    if (label.Status != QrLabelStatuses.InTransit)
                        throw PickingOperationException.Conflict("Carton không còn ở trạng thái đang luân chuyển.");
                }
            }
            else
            {
                var label = await db.BatchQrLabelRecords.SingleOrDefaultAsync(x => x.QrCode == cartonId, ct)
                    ?? throw PickingOperationException.NotFound("Không tìm thấy carton_id.");
                if (label.Quantity != 1 || label.CurrentWarehouseId != d.WarehouseId || label.Status != QrLabelStatuses.Available)
                    throw PickingOperationException.Conflict("Phải quét một carton vật lý khả dụng tại kho xuất.");
                var batch = await db.BatchRecords.SingleAsync(x => x.Id == label.BatchId, ct);
                await ValidBatch(batch, d.WarehouseId, ct);
                var candidates = await Lines.Where(x => x.DocumentId == id && x.BatchId == batch.Id && (!preferredLineId.HasValue || x.Id == preferredLineId.Value)).OrderBy(x => x.Id).ToArrayAsync(ct);
                WarehouseDocumentLine? target = null;
                foreach (var l in candidates)
                    if (await Cartons.CountAsync(x => x.LineId == l.Id, ct) < l.Quantity) { target = l; break; }
                if (target is null) throw PickingOperationException.Conflict("Sai batch hoặc đã quét đủ số carton cần lấy.");
                if (d.Reason == "Order")
                {
                    var list = await db.PickingListRecords.AsNoTracking().SingleAsync(x => x.Id == target.PickingListId, ct);
                    var allocationId = await db.PickingItemRecords.Where(x => x.Id == target.PickingItemId).Select(x => x.AllocationId).SingleAsync(ct);
                    await picking.ScanAsync(list.PickingCode, new(cartonId, 1, list.Version, label.Version, [new(allocationId, 1)]),
                        $"wd:{id:N}:{label.Id:N}:{d.Version}", actor, role, ct, documentWorkflow: true);
                    // Picking clears tracking after saving; reload the document before changing its version.
                    db.ChangeTracker.Clear();
                    d = await Find(id, ct);
                }
                else
                {
                    label.Status = QrLabelStatuses.Picked; label.Version++; label.UpdatedAt = clock.GetUtcNow();
                    Event(label, d, actor, "Picked", "Available", "Picked");
                }
                existing = new() { Id = Guid.NewGuid(), DocumentId = id, LineId = target.Id, CartonId = cartonId, LabelId = label.Id };
                Cartons.Add(existing);
            }
            existing.ScannedAt = clock.GetUtcNow(); existing.ScannedByUserId = actor;
            d.Version++;
            await db.SaveChangesAsync(ct);
            if (d.Reason == "Order")
            {
                var required = await Lines.Where(x => x.DocumentId == id).SumAsync(x => x.Quantity, ct);
                if (await Cartons.CountAsync(x => x.DocumentId == id && x.ScannedAt.HasValue, ct) == required)
                {
                    var listIds = await Lines.Where(x => x.DocumentId == id).Select(x => x.PickingListId!.Value).Distinct().ToArrayAsync(ct);
                    foreach (var listId in listIds)
                    {
                        var list = await db.PickingListRecords.AsNoTracking().SingleAsync(x => x.Id == listId, ct);
                        await picking.CompleteAsync(list.PickingCode, new(list.Version, null, null), actor, role, ct, documentWorkflow: true);
                    }
                    db.ChangeTracker.Clear(); d = await Find(id, ct);
                    foreach (var shipment in await db.ShipmentRecords.Where(x => listIds.Contains(x.PickingListId ?? Guid.Empty) && x.Status == ShipmentStatuses.WaitingToPick).ToArrayAsync(ct))
                    { shipment.TotalQuantity = await db.PickingItemRecords.Where(i => i.PickingListId == shipment.PickingListId).SumAsync(i => i.PickedQuantity, ct); shipment.Status = ShipmentStatuses.Ready; shipment.ReadyAt = clock.GetUtcNow(); shipment.Version++; shipment.UpdatedAt = clock.GetUtcNow(); }
                    d.Status = "Prepared"; d.Version++;
                    await db.SaveChangesAsync(ct);
                }
            }
            return id;
        }

    public Task<Guid> PostAsync(Guid id, int version, Guid actor, string role, CancellationToken ct) =>
        Atomic(async () =>
        {
            var workId = id;
            Keeper(role);
            var d = await Find(workId, ct);
            await Access(d.WarehouseId, actor, role, ct);
            if (d.Status == "Posted") return workId;
            if (d.Kind == "Picking" && d.Status == "Fulfilled")
                return await Documents.Where(x => x.SourceDocumentId == workId && x.Kind == "Issue").Select(x => x.Id).SingleAsync(ct);
            Version(d, version);
            if (d.Kind == "Picking")
            {
                var issue = await SnapshotIssue(d, actor, ct);
                d = issue; workId = issue.Id;
            }
            if (d.Kind == "Request" || (d.Reason == "Order" ? d.Status != "Prepared" : d.Status != "Open"))
                throw PickingOperationException.Conflict("Phiếu chưa đủ điều kiện ghi sổ.");
            var lines = await Lines.Where(x => x.DocumentId == workId).ToArrayAsync(ct);
            var cartons = await Cartons.Where(x => x.DocumentId == workId).ToArrayAsync(ct);
            if (lines.Length == 0) throw PickingOperationException.Conflict("Phiếu chưa nhận hàng; quét ít nhất một pallet.");
            foreach (var l in lines)
                if (cartons.Count(x => x.LineId == l.Id && x.ScannedAt.HasValue) != l.Quantity)
                    throw PickingOperationException.Conflict("Phải quét đủ từng carton trên phiếu trước khi ghi sổ.");
            if (d.Reason == "Order")
            {
                foreach (var listId in lines.Select(x => x.PickingListId!.Value).Distinct())
                {
                    var shipment = await db.ShipmentRecords.AsNoTracking().SingleAsync(x => x.PickingListId == listId, ct);
                    await shipments.ConfirmIssueAsync(shipment.Id, new(shipment.Version), actor, role, ct, documentWorkflow: true);
                }
                db.ChangeTracker.Clear(); d = await Find(workId, ct);
            }
            else if (d.Reason == "Factory" && d.GoodsReceiptId is null)
            {
                // Each pallet scan already posted its inventory atomically; closing never increments it again.
            }
            else if (d.Reason == "Factory")
            {
                await inventory.ApproveReceiptAsync(d.GoodsReceiptId!.Value, actor, role, ct, documentWorkflow: true);
                foreach (var l in lines)
                {
                    var batch = await db.BatchRecords.SingleAsync(x => x.WarehouseId == d.WarehouseId && x.ProductId == l.ProductId &&
                        x.FactoryProductionBatchId == null && x.ProductionDate == l.ProductionDate && x.ExpirationDate == l.ExpirationDate, ct);
                    l.BatchId = batch.Id; l.BatchCode = batch.BatchCode;
                    foreach (var c in cartons.Where(x => x.LineId == l.Id))
                    {
                        var label = new BatchQrLabel { Id = Guid.NewGuid(), BatchId = batch.Id, CurrentWarehouseId = d.WarehouseId,
                            GoodsReceiptId = d.GoodsReceiptId, QrCode = c.CartonId, Quantity = 1, Status = QrLabelStatuses.Available,
                            Version = 1, GeneratedAt = clock.GetUtcNow(), UpdatedAt = clock.GetUtcNow() };
                        label.RootLabelId = label.Id; c.LabelId = label.Id;
                        db.BatchQrLabelRecords.Add(label); Event(label, d, actor, "Received", "Pending", "Available");
                    }
                }
            }
            else
            {
                foreach (var l in lines)
                {
                    var sourceBatch = await db.BatchRecords.SingleAsync(x => x.Id == l.BatchId, ct);
                    Batch batch;
                    if (d.Kind == "Issue") { batch = sourceBatch; await ValidBatch(batch, d.WarehouseId, ct); }
                    else
                    {
                        batch = db.BatchRecords.Local.FirstOrDefault(x => x.WarehouseId == d.WarehouseId && x.ProductId == sourceBatch.ProductId &&
                            x.FactoryProductionBatchId == sourceBatch.FactoryProductionBatchId &&
                            (sourceBatch.FactoryProductionBatchId == null || x.WarehouseReceiptDate == FactoryTraceabilityService.VietnamDate(clock.GetUtcNow())) &&
                            x.ProductionDate == sourceBatch.ProductionDate && x.ExpirationDate == sourceBatch.ExpirationDate)
                            ?? await db.BatchRecords.SingleOrDefaultAsync(x => x.WarehouseId == d.WarehouseId && x.ProductId == sourceBatch.ProductId &&
                            x.FactoryProductionBatchId == sourceBatch.FactoryProductionBatchId &&
                            (sourceBatch.FactoryProductionBatchId == null || x.WarehouseReceiptDate == FactoryTraceabilityService.VietnamDate(clock.GetUtcNow())) &&
                            x.ProductionDate == sourceBatch.ProductionDate && x.ExpirationDate == sourceBatch.ExpirationDate, ct) ?? new Batch {
                                Id = Guid.NewGuid(), ParentBatchId = sourceBatch.Id, RootBatchId = sourceBatch.RootBatchId ?? sourceBatch.Id,
                                WarehouseId = d.WarehouseId, ProductId = sourceBatch.ProductId,
                                FactoryProductionBatchId = sourceBatch.FactoryProductionBatchId,
                                WarehouseReceiptDate = FactoryTraceabilityService.VietnamDate(clock.GetUtcNow()),
                                BatchCode = sourceBatch.FactoryProductionBatchId.HasValue
                                    ? FactoryTraceabilityService.StockCode((await db.WarehouseRecords.SingleAsync(x => x.Id == d.WarehouseId, ct)).Code,
                                        (await db.ProductRecords.SingleAsync(x => x.Id == sourceBatch.ProductId, ct)).Sku,
                                        (await db.Set<FactoryProductionBatch>().SingleAsync(x => x.Id == sourceBatch.FactoryProductionBatchId, ct)).Code,
                                        FactoryTraceabilityService.VietnamDate(clock.GetUtcNow()), sourceBatch.ExpirationDate)
                                    : $"TR-{Guid.NewGuid():N}", ProductionDate = sourceBatch.ProductionDate,
                                ExpirationDate = sourceBatch.ExpirationDate, ExpiryStatus = sourceBatch.ExpiryStatus,
                                StockStatus = BatchStockStatuses.InStock, CreatedAt = clock.GetUtcNow() };
                        if (db.Entry(batch).State == EntityState.Detached) db.BatchRecords.Add(batch);
                        l.BatchId = batch.Id; l.BatchCode = batch.BatchCode;
                    }
                    var balance = db.InventoryBalanceRecords.Local.FirstOrDefault(x => x.BatchId == batch.Id)
                        ?? await db.InventoryBalanceRecords.SingleOrDefaultAsync(x => x.BatchId == batch.Id, ct);
                    if (balance is null) { balance = new() { Id = Guid.NewGuid(), BatchId = batch.Id }; db.InventoryBalanceRecords.Add(balance); }
                    var delta = d.Kind == "Issue" ? -l.Quantity : l.Quantity;
                    if (d.Kind == "Issue" && (balance.PhysicalQuantity < l.Quantity || balance.ReservedQuantity < l.Quantity))
                        throw PickingOperationException.Conflict("Tồn giữ cho luân chuyển đã thay đổi.");
                    balance.PhysicalQuantity += delta;
                    if (d.Kind == "Issue") balance.ReservedQuantity -= l.Quantity;
                    balance.AvailableQuantity = balance.PhysicalQuantity - balance.ReservedQuantity;
                    if (balance.AvailableQuantity < 0) throw PickingOperationException.Conflict("Tồn khả dụng âm.");
                    balance.Version++; balance.UpdatedAt = clock.GetUtcNow();
                    batch.Quantity += delta; batch.Version++; batch.UpdatedAt = clock.GetUtcNow();
                    Ledger(d, batch, balance, actor, delta, d.Kind == "Issue" ? -l.Quantity : 0,
                        d.Kind == "Issue" ? InventoryTransactionTypes.TransferDispatch : InventoryTransactionTypes.TransferReceipt);
                    foreach (var c in cartons.Where(x => x.LineId == l.Id))
                    {
                        var label = await db.BatchQrLabelRecords.SingleAsync(x => x.Id == c.LabelId, ct);
                        var from = d.Kind == "Issue" ? QrLabelStatuses.Picked : QrLabelStatuses.InTransit;
                        if (label.Status != from) throw PickingOperationException.Conflict("Trạng thái carton đã thay đổi.");
                        label.BatchId = batch.Id; label.CurrentWarehouseId = d.WarehouseId;
                        label.Status = d.Kind == "Issue" ? QrLabelStatuses.InTransit : QrLabelStatuses.Available;
                        label.Version++; label.UpdatedAt = clock.GetUtcNow();
                        Event(label, d, actor, d.Kind == "Issue" ? "Issued" : "Received", from, label.Status);
                    }
                }
            }
            d.Status = "Posted"; d.PostedAt = clock.GetUtcNow(); d.Version++;
            if (d.SourceDocumentId.HasValue)
            {
                var source = await Find(d.SourceDocumentId.Value, ct);
                if (source.Kind is "Request" or "Picking") { source.Status = "Fulfilled"; source.Version++; }
                if (source.Kind == "Picking" && source.SourceDocumentId.HasValue)
                {
                    var request = await Find(source.SourceDocumentId.Value, ct);
                    request.Status = "Fulfilled"; request.Version++;
                }
            }
            await db.SaveChangesAsync(ct);
            return workId;
        }, ct);

    private async Task<Guid> ReceiveFactoryPallet(WarehouseDocument d, string code, int version, Guid actor, CancellationToken ct, ReceiptCartonInspection[]? inspection = null)
    {
        var cachedPallet = await db.Set<FactoryPallet>().SingleOrDefaultAsync(x => x.Code == code, ct);
        if (cachedPallet?.Status == "Received")
        {
            if (cachedPallet.ReceiptDocumentId == d.Id) {
                if (inspection is not null) {
                    var codes = await db.Set<FactoryCarton>().Where(x => x.PalletId == cachedPallet.Id).Select(x => x.Code).ToArrayAsync(ct);
                    string replay;
                    try { replay = ReceiptInspection.Normalize(codes, inspection); }
                    catch (ArgumentException e) { throw PickingOperationException.Validation(e.Message); }
                    if (replay != cachedPallet.ReceiptInspectionJson)
                        throw PickingOperationException.Conflict("Pallet đã nhận với kết quả khác; không được sửa biên bản đã ghi sổ.");
                }
                return d.Id;
            }
            throw PickingOperationException.Conflict("Pallet đã được nhập trên phiếu khác.");
        }
        var manifest = await factory.ManifestAsync(code, ct);
        var p = manifest.Pallet; var fb = manifest.Batch;
        RequireBoundPallet(d, p.Id);
        if (d.Status != "Open") throw PickingOperationException.Conflict("Phiếu đã đóng, không thể nhận thêm pallet.");
        Version(d, version);
        if (p.Status != "Dispatched" || p.DestinationWarehouseId != d.WarehouseId)
            throw PickingOperationException.Conflict("Pallet chưa xuất từ nhà máy, sai kho đích hoặc đã được nhận.");
        var today = FactoryTraceabilityService.VietnamDate(clock.GetUtcNow());
        if (fb.ExpirationDate < today || fb.ProductionDate > today || manifest.Cartons.Length == 0)
            throw PickingOperationException.Conflict("Pallet không có carton hoặc batch có ngày không hợp lệ.");
        string inspectionJson;
        try { inspectionJson = ReceiptInspection.Normalize(manifest.Cartons.Select(c => c.Code), inspection ??
            manifest.Cartons.Select(c => new ReceiptCartonInspection(c.Code, "Accepted", null, null)).ToArray()); }
        catch (ArgumentException e) { throw PickingOperationException.Validation(e.Message); }
        var accepted = ReceiptInspection.Read(inspectionJson).Where(x => x.Disposition == "Accepted").Select(x => x.CartonCode).ToHashSet();
        var product = await db.ProductRecords.SingleAsync(x => x.Id == fb.ProductId, ct);
        if (product.Status != "Published") throw PickingOperationException.Conflict("SKU đã ngừng phát hành.");
        if (product.Sku != manifest.Sku) throw PickingOperationException.Conflict("SKU trong manifest không khớp catalog kho.");
        // Import immutable factory identities as local snapshots; FKs stay entirely inside WMS.
        var cachedBatch = await db.Set<FactoryProductionBatch>().SingleOrDefaultAsync(x => x.Id == fb.Id, ct);
        if (cachedBatch is null) db.Add(fb);
        else {
            if (cachedBatch.Code != fb.Code || cachedBatch.ProductId != fb.ProductId || cachedBatch.ExpirationDate != fb.ExpirationDate || cachedBatch.ProductionDate != fb.ProductionDate)
                throw PickingOperationException.Conflict("Thông tin batch nguồn đã thay đổi; không được thay nguồn gốc hàng.");
            fb = cachedBatch;
        }
        if (cachedPallet is null) db.Add(p);
        else {
            cachedPallet.DestinationWarehouseId = p.DestinationWarehouseId;
            cachedPallet.DispatchedAt = p.DispatchedAt;
            p = cachedPallet;
        }
        var cartonIds = manifest.Cartons.Select(c => c.Id).ToArray();
        var cachedCartonIds = await db.Set<FactoryCarton>().Where(c => cartonIds.Contains(c.Id)).Select(c => c.Id).ToArrayAsync(ct);
        foreach (var carton in manifest.Cartons.Where(c => !cachedCartonIds.Contains(c.Id))) db.Add(carton);
        var warehouse = await db.WarehouseRecords.SingleAsync(x => x.Id == d.WarehouseId, ct);
        var batch = await db.BatchRecords.SingleOrDefaultAsync(x => x.WarehouseId == d.WarehouseId &&
            x.FactoryProductionBatchId == fb.Id && x.WarehouseReceiptDate == today, ct);
        if (batch is null)
        {
            var batchId = Guid.NewGuid();
            batch = new() { Id = batchId, RootBatchId = batchId, FactoryProductionBatchId = fb.Id,
                WarehouseReceiptDate = today, WarehouseId = d.WarehouseId, ProductId = fb.ProductId,
                BatchCode = FactoryTraceabilityService.StockCode(warehouse.Code, manifest.Sku, fb.Code, today, fb.ExpirationDate),
                ProductionDate = fb.ProductionDate, ExpirationDate = fb.ExpirationDate,
                ExpiryStatus = fb.ExpirationDate <= today.AddDays((int)Math.Ceiling(product.NearDateThresholdHours / 24m)) ? BatchExpiryStatuses.NearExpiry : BatchExpiryStatuses.Valid,
                StockStatus = BatchStockStatuses.InStock, CreatedAt = clock.GetUtcNow(), UpdatedAt = clock.GetUtcNow() };
            db.BatchRecords.Add(batch);
        }
        var balance = await db.InventoryBalanceRecords.SingleOrDefaultAsync(x => x.BatchId == batch.Id, ct);
        if (balance is null) { balance = new() { Id = Guid.NewGuid(), BatchId = batch.Id }; db.InventoryBalanceRecords.Add(balance); }
        var quantity = accepted.Count;
        batch.Quantity += quantity; batch.Version++; batch.UpdatedAt = clock.GetUtcNow();
        balance.PhysicalQuantity += quantity; balance.AvailableQuantity += quantity;
        balance.Version++; balance.UpdatedAt = clock.GetUtcNow();
        var line = await Lines.SingleOrDefaultAsync(x => x.DocumentId == d.Id && x.BatchId == batch.Id, ct);
        if (line is null)
        {
            line = await NewLine(d.Id, batch, 0, ct); line.SourceLotCode = fb.Code; Lines.Add(line);
        }
        line.Quantity += quantity;
        foreach (var c in manifest.Cartons.Where(c => accepted.Contains(c.Code)))
        {
            var label = new BatchQrLabel { Id = Guid.NewGuid(), BatchId = batch.Id, CurrentWarehouseId = d.WarehouseId,
                QrCode = c.Code, Quantity = 1, Status = QrLabelStatuses.Available, Version = 1,
                GeneratedAt = fb.CreatedAt, UpdatedAt = clock.GetUtcNow() };
            label.RootLabelId = label.Id; db.BatchQrLabelRecords.Add(label);
            Cartons.Add(new() { Id = Guid.NewGuid(), DocumentId = d.Id, LineId = line.Id, CartonId = c.Code,
                LabelId = label.Id, ScannedAt = clock.GetUtcNow(), ScannedByUserId = actor });
            Event(label, d, actor, "Received", "InTransit", "Available");
        }
        if (quantity > 0) Ledger(d, batch, balance, actor, quantity, 0, InventoryTransactionTypes.PurchaseReceipt);
        p.ReceiptInspectionJson = inspectionJson;
        p.Status = "Received"; p.ReceiptDocumentId = d.Id; p.ReceivedAt = clock.GetUtcNow(); p.Version++;
        db.Add(new FactoryReceiptSync { PalletId = p.Id, ReceiptDocumentId = d.Id, WarehouseId = d.WarehouseId, ReceivedAt = p.ReceivedAt.Value, ReceiptInspectionJson = inspectionJson });
        d.Version++;
        await db.SaveChangesAsync(ct);
        return d.Id;
    }

    public Task<Guid> CancelRequestAsync(Guid id, int version, Guid actor, string role, CancellationToken ct) =>
        Atomic(async () =>
        {
            if (role != SystemRoles.Admin) throw PickingOperationException.Forbidden("Chỉ admin được hủy yêu cầu kho.");
            var d = await Find(id, ct);
            if (d.Status == "Cancelled") return id;
            Version(d, version);
            if (d.Kind != "Request" || d.Status != "Open") throw PickingOperationException.Conflict("Chỉ hủy yêu cầu chưa xuất kho.");
            var child = await Documents.SingleOrDefaultAsync(x => x.SourceDocumentId == id, ct);
            if (child is not null)
            {
                if (await Cartons.AnyAsync(x => x.DocumentId == child.Id && x.ScannedAt.HasValue, ct))
                    throw PickingOperationException.Conflict("Đã lấy carton; phải xử lý hàng đã soạn trước khi hủy.");
                child.Status = "Cancelled"; child.Version++;
                if (d.Reason == "Order")
                {
                    var lists = await Lines.Where(x => x.DocumentId == child.Id).Select(x => x.PickingListId).ToArrayAsync(ct);
                    foreach (var list in await db.PickingListRecords.Where(x => lists.Contains(x.Id)).ToArrayAsync(ct))
                    {
                        list.Status = "Pending"; list.AssignedToUserId = null; list.StartedAt = null;
                        list.ConfirmedAt = null; list.ConfirmedByUserId = null; list.Version++; list.UpdatedAt = clock.GetUtcNow();
                    }
                }
            }
            if (d.Reason == "Transfer")
                foreach (var l in await Lines.Where(x => x.DocumentId == id).ToArrayAsync(ct))
                {
                    var batch = await db.BatchRecords.SingleAsync(x => x.Id == l.BatchId, ct);
                    var balance = await db.InventoryBalanceRecords.SingleAsync(x => x.BatchId == l.BatchId, ct);
                    if (balance.ReservedQuantity < l.Quantity) throw PickingOperationException.Conflict("Tồn giữ không còn khớp yêu cầu.");
                    balance.ReservedQuantity -= l.Quantity; balance.AvailableQuantity += l.Quantity;
                    balance.Version++; balance.UpdatedAt = clock.GetUtcNow();
                    Ledger(d, batch, balance, actor, 0, -l.Quantity, "TransferRelease");
                }
            d.Status = "Cancelled"; d.Version++;
            await db.SaveChangesAsync(ct);
            return id;
        }, ct);

    /// <summary>Relabel legacy aggregate QR stock into physical one-carton labels, preserving split lineage.</summary>
    public Task<Guid> PrepareCartonsAsync(Guid id, Guid actor, string role, CancellationToken ct) =>
        Atomic(async () =>
        {
            Keeper(role);
            var d = await Find(id, ct);
            await Access(d.WarehouseId, actor, role, ct);
            if (d.Kind is not ("Picking" or "Issue") || d.Status != "Open") throw PickingOperationException.Conflict("Chỉ chuẩn bị nhãn cho phiếu xuất đang mở.");
            var lines = await Lines.Where(x => x.DocumentId == id).ToArrayAsync(ct);
            foreach (var group in lines.GroupBy(x => x.BatchId!.Value))
            {
                var quantity = group.Sum(x => x.Quantity);
                if (quantity > 1000) throw PickingOperationException.Validation("In tối đa 1.000 carton mỗi batch trong một phiếu.");
                var batch = await db.BatchRecords.SingleAsync(x => x.Id == group.Key, ct);
                await ValidBatch(batch, d.WarehouseId, ct);
                if (batch.FactoryProductionBatchId.HasValue) continue;
                var available = await db.BatchQrLabelRecords.CountAsync(x => x.BatchId == group.Key && x.Status == QrLabelStatuses.Available && x.Quantity == 1, ct);
                while (available < quantity)
                {
                    var aggregate = await db.BatchQrLabelRecords.Where(x => x.BatchId == group.Key &&
                        x.Status == QrLabelStatuses.Available && x.Quantity > 1).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
                    if (aggregate is not null)
                    {
                        await qrLabels.SplitAsync(aggregate.QrCode, new(1, aggregate.Version), Guid.NewGuid().ToString(), actor, role, ct);
                        available += aggregate.Quantity == 2 ? 2 : 1;
                    }
                    else
                    {
                        await qrLabels.GenerateAsync(new(group.Key, 1, null), Guid.NewGuid().ToString(), actor, role, ct);
                        available++;
                    }
                }
            }
            return id;
        }, ct);

    private Task<WarehousePalletMapping[]> PalletMappings(string[] codes, CancellationToken ct) =>
        (from c in db.Set<FactoryCarton>().AsNoTracking()
         join p in db.Set<FactoryPallet>().AsNoTracking() on c.PalletId equals p.Id
         join b in db.Set<FactoryProductionBatch>().AsNoTracking() on p.ProductionBatchId equals b.Id
         where codes.Contains(c.Code)
         select new WarehousePalletMapping(p.Code, c.Code, b.UnitsPerCarton)).ToArrayAsync(ct);

    private async Task<WarehouseDocumentLine> NewLine(Guid document, Batch batch, decimal quantity, CancellationToken ct)
    {
        var p = await db.ProductRecords.SingleAsync(x => x.Id == batch.ProductId, ct);
        return new() { Id = Guid.NewGuid(), DocumentId = document, BatchId = batch.Id, BatchCode = batch.BatchCode,
            ProductId = p.Id, Sku = p.Sku, ProductName = p.Name, Quantity = quantity,
            ProductionDate = batch.ProductionDate, ExpirationDate = batch.ExpirationDate,
            SourceLotCode = batch.FactoryProductionBatchId.HasValue ? db.Set<FactoryProductionBatch>().Local.FirstOrDefault(x => x.Id == batch.FactoryProductionBatchId)?.Code ?? (await db.Set<FactoryProductionBatch>().SingleAsync(x => x.Id == batch.FactoryProductionBatchId, ct)).Code : null };
    }
    private WarehouseDocument New<T>(string kind, string reason, Guid wh, string key, T request, Guid actor, string? note)
    {
        var id = Guid.NewGuid();
        return new() { Id = id, Code = $"{(kind == "Request" ? "YCK" : kind == "Picking" ? "PSO" : kind == "Issue" ? "PXK" : "PNK")}-{id:N}",
            Kind = kind, Reason = reason, WarehouseId = wh, CreatedByUserId = actor, CreatedAt = clock.GetUtcNow(),
            OperationKey = key, RequestHash = Hash(request, actor), Note = note };
    }
    private static string Hash<T>(T request, Guid actor) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { actor, request }))));
    private async Task<Guid?> Replay<T>(string key, T request, Guid actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw PickingOperationException.Validation("Idempotency-Key cần 1–100 ký tự.");
        var prior = await Documents.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == key, ct);
        if (prior is null) return null;
        if (prior.RequestHash != Hash(request, actor)) throw PickingOperationException.Conflict("Khóa thao tác đã được dùng cho nội dung khác.");
        return prior.Id;
    }
    private async Task<WarehouseDocument> Find(Guid id, CancellationToken ct) =>
        await Documents.SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw PickingOperationException.NotFound("Không tìm thấy chứng từ kho.");
    private async Task<bool> HasAccess(Guid wh, Guid actor, string role, CancellationToken ct) => role == SystemRoles.Admin ||
        role == SystemRoles.WarehouseKeeper && await ReportingScopeQueries.ManagedWarehouses(db, actor).AnyAsync(x => x.Id == wh, ct);
    private async Task Access(Guid wh, Guid actor, string role, CancellationToken ct)
    {
        if (!await HasAccess(wh, actor, role, ct)) throw PickingOperationException.Forbidden("Kho không thuộc phạm vi quản lý.");
        if (!await db.WarehouseRecords.AnyAsync(x => x.Id == wh && x.IsActive, ct)) throw PickingOperationException.Conflict("Kho không hoạt động.");
    }
    private async Task ValidBatch(Batch batch, Guid wh, CancellationToken ct)
    {
        if (batch.WarehouseId != wh || batch.ExpirationDate < FactoryTraceabilityService.VietnamDate(clock.GetUtcNow()) || batch.ExpiryStatus == "Expired")
            throw PickingOperationException.Conflict("Batch sai kho hoặc hết hạn.");
        if (await db.InventoryLockRecords.AnyAsync(x => x.BatchId == batch.Id && x.Status == "Active", ct))
            throw PickingOperationException.Conflict("Batch đang bị khóa; giải quyết khóa trước khi lấy hàng.");
    }
    private static void Keeper(string role) { if (role != SystemRoles.WarehouseKeeper) throw PickingOperationException.Forbidden("Chỉ thủ kho được lập và thực hiện phiếu kho."); }
    private static void Version(WarehouseDocument d, int v) { if (d.Version != v) throw PickingOperationException.Conflict("Phiếu đã thay đổi, tải lại trước khi thao tác."); }
    public static void WholeCartons(decimal quantity) { if (quantity <= 0 || quantity != decimal.Truncate(quantity)) throw PickingOperationException.Validation("Số carton phải là số nguyên dương."); }
    private void Event(BatchQrLabel label, WarehouseDocument d, Guid actor, string action, string from, string to) =>
        db.QrLabelEventRecords.Add(new() { Id = Guid.NewGuid(), BatchQrLabelId = label.Id, WarehouseId = d.WarehouseId,
            PerformedByUserId = actor, OperationKey = $"wd:{d.Id:N}:{label.Id:N}:{d.Version}:{action}", EventType = action,
            FromStatus = from, ToStatus = to, Quantity = 1, ReferenceCode = d.Code, OccurredAt = clock.GetUtcNow() });
    private void Ledger(WarehouseDocument d, Batch batch, InventoryBalance balance, Guid actor, decimal physical, decimal reserved, string type) =>
        db.InventoryTransactionRecords.Add(new() { Id = Guid.NewGuid(), BatchId = batch.Id, WarehouseId = batch.WarehouseId,
            ProductId = batch.ProductId, CreatedByUserId = actor, TransactionType = type, SourceType = "WarehouseDocument",
            SourceId = d.Id, ReferenceCode = d.Code, PhysicalQuantityDelta = physical, ReservedQuantityDelta = reserved,
            PhysicalQuantityAfter = balance.PhysicalQuantity, ReservedQuantityAfter = balance.ReservedQuantity,
            AvailableQuantityAfter = balance.AvailableQuantity, OccurredAt = clock.GetUtcNow() });
    private async Task<T> Atomic<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var result = await operation();
                await tx.CommitAsync(ct);
                return result;
            });
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        { throw PickingOperationException.Conflict("Có thao tác đồng thời; tải lại và quét/thử lại. Không cộng tồn hai lần."); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        { throw PickingOperationException.Conflict("Có thao tác đồng thời; tải lại và quét/thử lại. Không cộng tồn hai lần."); }
        catch (DbUpdateConcurrencyException) { throw PickingOperationException.Conflict("Dữ liệu kho đã thay đổi, tải lại và thử lại."); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw PickingOperationException.Conflict("Chứng từ hoặc carton đã được dùng; tải lại phiếu."); }
    }
}
