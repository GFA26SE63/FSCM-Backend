using FSCM.Application.Features.Picking;
using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.WarehouseWork;

public sealed record InspectedPalletRequest(string PalletCode, int ExpectedVersion, ReceiptCartonInspection[] Cartons);
public sealed record PalletReceiptReport(string PalletCode, ReceiptCartonInspection[] Cartons);

public sealed partial class WarehouseWorkService
{
    public async Task<PalletManifest> PreviewPalletAsync(Guid id, string code, Guid actor, string role, CancellationToken ct)
    {
        Keeper(role);
        var d = await Find(id, ct);
        await Access(d.WarehouseId, actor, role, ct);
        if (d.Kind != "Receipt" || d.Reason != "Factory" || d.GoodsReceiptId != null || d.Status != "Open")
            throw PickingOperationException.Conflict("Chỉ kiểm đếm trên phiếu nhập nhà máy đang mở.");
        var manifest = await factory.ManifestAsync(code.Trim(), ct);
        if (manifest.Pallet.DestinationWarehouseId != d.WarehouseId || manifest.Pallet.Status != "Dispatched")
            throw PickingOperationException.Conflict("Pallet chưa xuất hoặc không thuộc kho nhận.");
        return manifest;
    }

    public Task<Guid> ReceiveInspectedPalletAsync(Guid id, InspectedPalletRequest request, Guid actor, string role, CancellationToken ct) => Atomic(async () => {
        Keeper(role);
        var d = await Find(id, ct);
        await Access(d.WarehouseId, actor, role, ct);
        if (d.Kind != "Receipt" || d.Reason != "Factory" || d.GoodsReceiptId != null)
            throw PickingOperationException.Conflict("Phiếu không nhận pallet nhà máy.");
        if (request.Cartons is null || string.IsNullOrWhiteSpace(request.PalletCode)) throw PickingOperationException.Validation("Thiếu pallet hoặc kết quả kiểm đếm.");
        return await ReceiveFactoryPallet(d, request.PalletCode.Trim(), request.ExpectedVersion, actor, ct, request.Cartons);
    }, ct);

    public async Task<PalletReceiptReport[]> ReceiptReportsAsync(Guid id, Guid actor, string role, CancellationToken ct)
    {
        var d = await Find(id, ct);
        await Access(d.WarehouseId, actor, role, ct);
        var pallets = await db.Set<FactoryPallet>().AsNoTracking().Where(x => x.ReceiptDocumentId == id && x.ReceiptInspectionJson != null).ToArrayAsync(ct);
        return pallets.Select(x => new PalletReceiptReport(x.Code, ReceiptInspection.Read(x.ReceiptInspectionJson!))).ToArray();
    }
}
