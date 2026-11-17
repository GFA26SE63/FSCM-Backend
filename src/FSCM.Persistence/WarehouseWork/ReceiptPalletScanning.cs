using System.Text.Json;
using FSCM.Application.Features.Picking;
using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.WarehouseWork;

public sealed record ReceiptPalletScanRequest(string Code, int ExpectedVersion, string Source);
public sealed record ReceiptPalletScan(Guid Id, string PalletCode, Guid ScannedByUserId,
    DateTimeOffset ScannedAt, string Source, int DocumentVersion, PalletManifest? Manifest = null, string? ScannedByName = null);

public sealed partial class WarehouseWorkService
{
    public Task<ReceiptPalletScan> ScanReceiptPalletAsync(Guid id, ReceiptPalletScanRequest request,
        Guid actor, string role, string? device, CancellationToken ct) => Atomic(async () =>
    {
        if (request.Source != "Camera" || string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 200)
            throw PickingOperationException.Validation("Hãy quét QR pallet bằng camera trên phiếu nhập.");
        var manifest = await PreviewPalletAsync(id, request.Code, actor, role, ct);
        if (manifest.Pallet.Code != request.Code.Trim())
            throw PickingOperationException.Conflict("Phải quét mã QR pallet, không dùng mã carton hoặc sản phẩm.");
        if (await db.Set<FactoryPallet>().AnyAsync(p => p.Id == manifest.Pallet.Id && p.Status == "Received", ct))
            throw PickingOperationException.Conflict("Pallet đã được nhận; không được quét nhập lại.");
        var d = await Find(id, ct);
        Version(d, request.ExpectedVersion);
        d.Version++;
        var scan = new ReceiptPalletScan(Guid.NewGuid(), manifest.Pallet.Code, actor,
            clock.GetUtcNow(), request.Source, d.Version, ScannedByName:
            await db.UserAccountRecords.Where(u => u.Id == actor).Select(u => u.FullName).SingleAsync(ct));
        db.Add(new AuditLog { Id = scan.Id, UserAccountId = actor, EntityName = "WarehouseDocument",
            EntityId = id, Action = "ReceiptPalletScanned", NewValues = JsonSerializer.Serialize(scan),
            Reason = device is null ? null : device[..Math.Min(device.Length, 300)], CreatedAt = scan.ScannedAt });
        await db.SaveChangesAsync(ct);
        return scan with { Manifest = manifest };
    }, ct);

    public async Task<ReceiptPalletScan[]> ReceiptScansAsync(Guid id, Guid actor, string role, CancellationToken ct)
    {
        var d = await Find(id, ct);
        await Access(d.WarehouseId, actor, role, ct);
        var events = await db.Set<AuditLog>().AsNoTracking().Where(e => e.EntityName == "WarehouseDocument"
            && e.EntityId == id && e.Action == "ReceiptPalletScanned").OrderBy(e => e.CreatedAt).ToArrayAsync(ct);
        return events.Select(e => JsonSerializer.Deserialize<ReceiptPalletScan>(e.NewValues!)!).ToArray();
    }

    public async Task<(string Warehouse, string Creator)> ReceiptPrintInfoAsync(Guid id, Guid actor, string role, CancellationToken ct)
    {
        var d = await Find(id, ct);
        await Access(d.WarehouseId, actor, role, ct);
        if (d.Kind != "Receipt") throw PickingOperationException.Conflict("Chứng từ không phải phiếu nhập.");
        return (await db.WarehouseRecords.Where(w => w.Id == d.WarehouseId).Select(w => w.Name).SingleAsync(ct),
            await db.UserAccountRecords.Where(u => u.Id == d.CreatedByUserId).Select(u => u.FullName).SingleAsync(ct));
    }

    private async Task RequireReceiptScan(Guid documentId, InspectedPalletRequest request, Guid actor, CancellationToken ct)
    {
        var log = await db.Set<AuditLog>().AsNoTracking().SingleOrDefaultAsync(e => e.Id == request.ScanId
            && e.EntityId == documentId && e.EntityName == "WarehouseDocument" && e.Action == "ReceiptPalletScanned"
            && e.UserAccountId == actor, ct);
        var scan = log?.NewValues is null ? null : JsonSerializer.Deserialize<ReceiptPalletScan>(log.NewValues);
        if (scan is null || scan.PalletCode != request.PalletCode.Trim() || scan.DocumentVersion != request.ExpectedVersion)
            throw PickingOperationException.Conflict("Chưa có lần quét pallet hợp lệ của bạn cho phiếu này. Hãy quét lại bằng camera.");
    }
}
