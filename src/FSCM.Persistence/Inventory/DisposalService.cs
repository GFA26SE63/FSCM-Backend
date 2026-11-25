using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FSCM.Application.Features.Inventory;
using FSCM.Application.Features.InventoryLocks;
using FSCM.Application.Features.QrLabels;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FSCM.Persistence.Inventory;

public sealed record DisposalInput(string[] BatchCodes, string Method, Guid OperationId);
public sealed record DisposalLine(string Sku, string ProductName, string BatchCode,
    DateOnly Expiry, decimal Qty);
public sealed record DisposalResult(string Code, string WarehouseCode, string WarehouseName,
    DateTimeOffset CreatedAt, string CreatedBy, string Reason, string Method, int BatchCount,
    decimal TotalQty, decimal? TotalValue, DisposalLine[] Lines);
public sealed record DisposalCandidate(string BatchCode, string WarehouseCode, string WarehouseName,
    string Sku, string ProductName, DateOnly Expiry, int DaysOverdue, decimal Qty, decimal? Value);

/// <summary>Whole-batch expiry write-offs share the inventory ledger, locks and physical QR history.</summary>
public sealed class DisposalService(AppDbContext db, TimeProvider clock)
{
    private sealed record Metadata(string Method, string RequestHash);
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
    private IQueryable<Warehouse> Warehouses(Guid actor, string role) =>
        role is SystemRoles.Admin or SystemRoles.Operator
            ? db.WarehouseRecords.AsNoTracking()
            : ReportingScopeQueries.ManagedWarehouses(db, actor);

    public async Task<DisposalCandidate[]> CandidatesAsync(Guid actor, string role, CancellationToken ct)
    {
        var today = Today;
        var rows = await (from batch in db.BatchRecords.AsNoTracking()
            join balance in db.InventoryBalanceRecords.AsNoTracking() on batch.Id equals balance.BatchId
            join warehouse in Warehouses(actor, role) on batch.WarehouseId equals warehouse.Id
            join product in db.ProductRecords.AsNoTracking() on batch.ProductId equals product.Id
            where warehouse.IsActive && batch.ExpirationDate < today && balance.PhysicalQuantity > 0
                && balance.ReservedQuantity == 0
                && !db.Set<BatchQrLabel>().Any(q => q.BatchId == batch.Id && q.Status == QrLabelStatuses.Picked)
                && !db.Set<InventoryLock>().Any(l => l.BatchId == batch.Id && l.Status == InventoryLockStatuses.Active
                    && l.RequiresResolution)
            orderby batch.ExpirationDate, batch.BatchCode
            select new { batch.BatchCode, warehouse.Code, WarehouseName = warehouse.Name,
                product.Sku, ProductName = product.Name, batch.ExpirationDate, balance.PhysicalQuantity }).ToArrayAsync(ct);
        return rows.Select(x => new DisposalCandidate(x.BatchCode, x.Code, x.WarehouseName,
            x.Sku, x.ProductName, x.ExpirationDate, today.DayNumber - x.ExpirationDate.DayNumber,
            x.PhysicalQuantity, null)).ToArray();
    }

    public async Task<DisposalResult[]> ListAsync(Guid actor, string role, string? code, CancellationToken ct)
    {
        var headers = await (from document in db.WriteOffRecords.AsNoTracking()
            join warehouse in Warehouses(actor, role) on document.WarehouseId equals warehouse.Id
            join user in db.UserAccountRecords.AsNoTracking() on document.CreatedByUserId equals user.Id
            where code == null || document.WriteOffCode == code
            orderby document.CreatedAt descending
            select new { Document = document, Warehouse = warehouse, User = user }).ToArrayAsync(ct);
        var ids = headers.Select(x => x.Document.Id).ToArray();
        var lines = await (from line in db.WriteOffItemRecords.AsNoTracking()
            join batch in db.BatchRecords.AsNoTracking() on line.BatchId equals batch.Id
            join product in db.ProductRecords.AsNoTracking() on batch.ProductId equals product.Id
            where ids.Contains(line.WriteOffId)
            select new { line.WriteOffId, Line = new DisposalLine(product.Sku, product.Name,
                batch.BatchCode, batch.ExpirationDate, line.Quantity) }).ToArrayAsync(ct);
        return headers.Select(x =>
        {
            var items = lines.Where(l => l.WriteOffId == x.Document.Id).Select(l => l.Line).ToArray();
            var metadata = ReadMetadata(x.Document.Reason);
            return new DisposalResult(x.Document.WriteOffCode, x.Warehouse.Code, x.Warehouse.Name,
                x.Document.CreatedAt, x.User.FullName, "Xử lý hàng hết hạn", metadata?.Method ?? "Tiêu hủy tại kho",
                items.Length, items.Sum(l => l.Qty), null, items);
        }).ToArray();
    }

    public async Task<DisposalResult[]> CreateAsync(DisposalInput input, Guid actor, string role, CancellationToken ct)
    {
        if (input.OperationId == Guid.Empty || input.BatchCodes is null || input.BatchCodes.Length is < 1 or > 100
            || input.BatchCodes.Any(string.IsNullOrWhiteSpace)
            || input.BatchCodes.Distinct(StringComparer.Ordinal).Count() != input.BatchCodes.Length)
            throw InventoryOperationException.Validation("Chọn từ 1 đến 100 batch khác nhau và mã thao tác hợp lệ.");
        if (input.Method is not ("Tiêu hủy tại kho" or "Trả nhà cung cấp"))
            throw InventoryOperationException.Validation("Hình thức xử lý không hợp lệ.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { Codes = input.BatchCodes.OrderBy(x => x, StringComparer.Ordinal).ToArray(), input.Method }))));
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var batches = await db.BatchRecords.Where(b => input.BatchCodes.Contains(b.BatchCode))
                    .OrderBy(b => b.Id).ToArrayAsync(ct);
                if (batches.Length != input.BatchCodes.Length)
                    throw InventoryOperationException.NotFound("Một hoặc nhiều batch không tồn tại.");
                var warehouseIds = batches.Select(b => b.WarehouseId).Distinct().ToArray();
                if (await Warehouses(actor, role).CountAsync(w => warehouseIds.Contains(w.Id) && w.IsActive, ct) != warehouseIds.Length)
                    throw InventoryOperationException.Forbidden("Chỉ được xử lý batch tại kho đang hoạt động được giao.");
                var prefix = $"HUY-{input.OperationId:N}";
                // One operation can create multiple warehouse documents; their deterministic IDs survive retries.
                var expectedIds = warehouseIds.Select(w => DocumentId(input.OperationId, actor, w)).ToArray();
                var existing = await db.WriteOffRecords.Where(w => expectedIds.Contains(w.Id)).ToArrayAsync(ct);
                // Detect reuse of the same operation with another warehouse selection as well.
                var operationDocs = await db.WriteOffRecords.Where(w => w.Reason != null && w.Reason.Contains(prefix)).ToArrayAsync(ct);
                if (operationDocs.Length > 0)
                {
                    if (existing.Length != expectedIds.Length || operationDocs.Length != expectedIds.Length
                        || existing.Any(w => ReadMetadata(w.Reason)?.RequestHash != hash))
                        throw InventoryOperationException.Conflict("Mã thao tác đã được dùng cho nội dung khác.");
                    var result = await ListAsync(actor, role, null, ct);
                    return result.Where(r => existing.Any(w => w.WriteOffCode == r.Code)).ToArray();
                }
                var now = clock.GetUtcNow();
                var today = Today;
                var documents = new List<WriteOff>();
                foreach (var group in batches.GroupBy(b => b.WarehouseId))
                {
                    var id = DocumentId(input.OperationId, actor, group.Key);
                    var document = new WriteOff { Id = id, WriteOffCode = $"HUY-{id:N}", WarehouseId = group.Key,
                        CreatedByUserId = actor, Status = "Disposed",
                        Reason = JsonSerializer.Serialize(new { Method = input.Method, RequestHash = hash, Operation = prefix }),
                        CreatedAt = now, UpdatedAt = now };
                    db.WriteOffRecords.Add(document);
                    documents.Add(document);
                    foreach (var batch in group)
                    {
                        var balance = await db.InventoryBalanceRecords.SingleOrDefaultAsync(b => b.BatchId == batch.Id, ct)
                            ?? throw InventoryOperationException.Conflict("Batch không có sổ tồn kho.");
                        var labels = await db.Set<BatchQrLabel>().Where(q => q.BatchId == batch.Id
                            && (q.Status == QrLabelStatuses.Available || q.Status == QrLabelStatuses.Picked)).ToArrayAsync(ct);
                        var locks = await db.Set<InventoryLock>().Where(l => l.BatchId == batch.Id
                            && l.Status == InventoryLockStatuses.Active).ToArrayAsync(ct);
                        if (batch.ExpirationDate >= today || balance.PhysicalQuantity <= 0)
                            throw InventoryOperationException.Conflict("Chỉ được xử lý batch đã quá HSD và còn tồn thực tế.");
                        if (balance.ReservedQuantity != 0 || labels.Any(q => q.Status == QrLabelStatuses.Picked)
                            || locks.Any(l => l.RequiresResolution))
                            throw InventoryOperationException.Conflict("Batch còn giữ chỗ, đang soạn hoặc có khóa cần xử lý trước khi hủy.");
                        if (batch.Quantity != balance.PhysicalQuantity)
                            throw InventoryOperationException.Conflict("Số lượng batch và sổ tồn không khớp; cần đối soát trước khi hủy.");
                        var quantity = balance.PhysicalQuantity;
                        db.WriteOffItemRecords.Add(new WriteOffItem { Id = Guid.NewGuid(), WriteOffId = id,
                            BatchId = batch.Id, Quantity = quantity, Reason = input.Method, CreatedAt = now });
                        balance.PhysicalQuantity = 0; balance.AvailableQuantity = 0; balance.Version++; balance.UpdatedAt = now;
                        batch.Quantity = 0; batch.ExpiryStatus = BatchExpiryStatuses.Expired;
                        batch.StockStatus = BatchStockStatuses.Depleted; batch.Version++; batch.UpdatedAt = now;
                        foreach (var inventoryLock in locks)
                        {
                            inventoryLock.Status = InventoryLockStatuses.WrittenOff;
                            inventoryLock.RequiresResolution = false; inventoryLock.ReleasedByUserId = actor;
                            inventoryLock.ReleasedAt = now; inventoryLock.UpdatedAt = now; inventoryLock.Version++;
                        }
                        foreach (var label in labels)
                        {
                            var from = label.Status;
                            label.Status = QrLabelStatuses.Invalidated; label.InvalidatedAt = now;
                            label.Version++; label.UpdatedAt = now;
                            db.Set<QrLabelEvent>().Add(new QrLabelEvent { Id = Guid.NewGuid(), BatchQrLabelId = label.Id,
                                WarehouseId = group.Key, PerformedByUserId = actor, OperationKey = $"writeoff:{id:N}:{label.Id:N}",
                                EventType = QrLabelEventTypes.Invalidated, FromStatus = from, ToStatus = label.Status,
                                Quantity = label.Quantity, ReferenceCode = document.WriteOffCode, Note = input.Method, OccurredAt = now });
                        }
                        db.Set<InventoryTransaction>().Add(new InventoryTransaction { Id = Guid.NewGuid(), BatchId = batch.Id,
                            WarehouseId = group.Key, ProductId = batch.ProductId, CreatedByUserId = actor,
                            TransactionType = InventoryTransactionTypes.WriteOff, SourceType = "WriteOff", SourceId = id,
                            ReferenceCode = document.WriteOffCode, PhysicalQuantityDelta = -quantity,
                            PhysicalQuantityAfter = 0, ReservedQuantityAfter = 0, AvailableQuantityAfter = 0,
                            Note = input.Method, OccurredAt = now });
                    }
                }
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                var all = await ListAsync(actor, role, null, ct);
                return all.Where(r => documents.Any(w => w.WriteOffCode == r.Code)).ToArray();
            }
            catch (Exception ex) when (IsStorageConflict(ex))
            {
                throw InventoryOperationException.Conflict("Tồn kho vừa thay đổi. Tải lại và thử lại cùng mã thao tác.");
            }
        });
    }

    private static bool IsStorageConflict(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException => true,
        PostgresException { SqlState: "40001" or "40P01" or "23505" } => true,
        _ when exception.InnerException is not null => IsStorageConflict(exception.InnerException),
        _ => false
    };

    private static Guid DocumentId(Guid operation, Guid actor, Guid warehouse) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{operation:N}:{actor:N}:{warehouse:N}"))[..16]);
    private static Metadata? ReadMetadata(string? reason)
    {
        try { return reason is null ? null : JsonSerializer.Deserialize<Metadata>(reason); }
        catch (JsonException) { return null; }
    }
}
