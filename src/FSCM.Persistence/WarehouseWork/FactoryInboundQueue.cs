using FSCM.Application.Features.Picking;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.WarehouseWork;

public sealed record FactoryInboundPallet(Guid PalletId, string PalletCode, Guid DestinationWarehouseId,
    DateTimeOffset? DispatchedAt, string BatchCode, string Sku, string ProductName, int CartonCount,
    DateOnly ExpirationDate, Guid? ReceiptDocumentId = null,
    // Filled by Traceability for pallets dispatched on a dispatch note (PXN); null for older pallets.
    Guid? DispatchNoteId = null, string? DispatchCode = null, string? VehicleNumber = null, string? DriverName = null);
public sealed record PrepareFactoryReceiptRequest(string PalletCode);

public sealed partial class WarehouseWorkService
{
    // One receipt per source pallet, shared across operators and browser retries.
    private static string FactoryPalletKey(Guid palletId) => $"factory-pallet:{palletId:N}";

    public async Task<FactoryInboundPallet[]> PendingFactoryAsync(Guid actor, string role, CancellationToken ct)
    {
        if (role is not (SystemRoles.Admin or SystemRoles.WarehouseKeeper))
            throw PickingOperationException.Forbidden("Không có quyền xem hàng chờ nhận.");
        var warehouses = role == SystemRoles.Admin ? db.WarehouseRecords.AsQueryable() : ReportingScopeQueries.ManagedWarehouses(db, actor);
        var allowed = await warehouses.Where(w => w.IsActive).Select(w => w.Id).ToArrayAsync(ct);
        if (allowed.Length == 0) return [];
        var pending = await factory.DispatchedAsync(allowed, ct);
        var palletIds = pending.Select(p => p.PalletId).ToArray();
        var received = await db.Set<FactoryPallet>().AsNoTracking()
            .Where(p => palletIds.Contains(p.Id) && p.Status == "Received").Select(p => p.Id).ToArrayAsync(ct);
        var keys = pending.Select(p => FactoryPalletKey(p.PalletId)).ToArray();
        var documents = await Documents.AsNoTracking().Where(d => keys.Contains(d.OperationKey)).ToArrayAsync(ct);
        return pending.Where(p => allowed.Contains(p.DestinationWarehouseId) && !received.Contains(p.PalletId))
            .Select(p => p with { ReceiptDocumentId = documents.SingleOrDefault(d => d.OperationKey == FactoryPalletKey(p.PalletId))?.Id })
            .ToArray();
    }

    public Task<Guid> PrepareFactoryAsync(PrepareFactoryReceiptRequest request, Guid actor, string role, CancellationToken ct)
    {
        Keeper(role);
        return Atomic(async () =>
        {
            if (string.IsNullOrWhiteSpace(request.PalletCode)) throw PickingOperationException.Validation("Chọn pallet cần nhận.");
            var manifest = await factory.ManifestAsync(request.PalletCode.Trim(), ct);
            var pallet = manifest.Pallet;
            if (pallet.DestinationWarehouseId is not Guid warehouseId)
                throw PickingOperationException.Conflict("Pallet chưa được nhà máy chỉ định kho nhận.");
            await Access(warehouseId, actor, role, ct);
            if (!await db.WarehouseRecords.AnyAsync(w => w.Id == warehouseId && w.IsActive, ct))
                throw PickingOperationException.Conflict("Kho nhận đã ngừng hoạt động.");
            var received = await db.Set<FactoryPallet>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == pallet.Id && p.Status == "Received", ct);
            if (received?.ReceiptDocumentId is Guid receiptId) return receiptId;
            if (pallet.Status != "Dispatched") throw PickingOperationException.Conflict("Pallet chưa xuất hoặc đã được nhận.");
            var key = FactoryPalletKey(pallet.Id);
            var prior = await Documents.SingleOrDefaultAsync(d => d.OperationKey == key, ct);
            if (prior is not null)
            {
                if (prior.WarehouseId != warehouseId || prior.Kind != "Receipt" || prior.Reason != "Factory")
                    throw PickingOperationException.Conflict("Phiếu nhập không khớp pallet nguồn.");
                return prior.Id;
            }
            var document = New("Receipt", "Factory", warehouseId, key, request, actor, $"Nhận pallet {pallet.Code}");
            Documents.Add(document);
            await db.SaveChangesAsync(ct);
            return document.Id;
        }, ct);
    }

    private static void RequireBoundPallet(WarehouseDocument document, Guid palletId)
    {
        if (document.OperationKey.StartsWith("factory-pallet:", StringComparison.Ordinal)
            && document.OperationKey != FactoryPalletKey(palletId))
            throw PickingOperationException.Conflict("Phiếu nhập này chỉ nhận pallet đã chọn từ hàng chờ nhận.");
    }
}
