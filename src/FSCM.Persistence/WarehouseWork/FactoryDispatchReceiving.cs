using System.Text.Json;
using FSCM.Application.Features.Picking;
using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.WarehouseWork;

/// <summary>
/// One row of "Hàng chờ nhận từ nhà máy": all pallets of one factory dispatch note (PXN) that still wait at the
/// warehouse. Pallets dispatched before dispatch notes existed form a group of one (<see cref="DispatchNoteId"/> null).
/// </summary>
public sealed record FactoryInboundDispatch(string Key, Guid? DispatchNoteId, string DispatchCode, Guid DestinationWarehouseId,
    DateTimeOffset? DispatchedAt, string? VehicleNumber, string? DriverName, FactoryInboundPallet[] Pallets, Guid? ReceiptDocumentId);

public sealed record PrepareFactoryDispatchReceiptRequest(Guid DispatchNoteId);

/// <summary>A pallet the receipt (PNK) must receive; <see cref="Received"/> once its carton check was confirmed on this PNK.</summary>
/// <remarks><see cref="Missing"/>: the whole pallet was recorded as "Thiếu" (never arrived); it counts as handled.</remarks>
public sealed record FactoryExpectedPallet(Guid PalletId, string PalletCode, string BatchCode, string Sku, string ProductName,
    int CartonCount, DateOnly ExpirationDate, bool Received = false, DateTimeOffset? ReceivedAt = null, bool Missing = false);

/// <summary>Records a whole pallet of the dispatch note as missing: every carton "Thiếu" with the same reason.</summary>
public sealed record MissingPalletRequest(string PalletCode, int ExpectedVersion, string Reason);

/// <summary>Dispatch note behind a factory PNK and the pallets it must receive before it can be completed.</summary>
public sealed record FactoryReceiptExpectation(Guid DispatchNoteId, string DispatchCode, string? VehicleNumber, string? DriverName,
    FactoryExpectedPallet[] Pallets)
{
    public bool AllReceived => Pallets.Length > 0 && Pallets.All(p => p.Received);
}

public sealed partial class WarehouseWorkService
{
    // One receipt per factory dispatch note, shared across operators and browser retries.
    private static string FactoryDispatchKey(Guid dispatchNoteId) => $"factory-dispatch:{dispatchNoteId:N}";
    private const string ExpectedPalletsAction = "FactoryReceiptExpectedPallets";

    /// <summary>Pure grouping of pending pallets into one row per dispatch note (legacy pallets stay one per row).</summary>
    public static FactoryInboundDispatch[] GroupFactoryDispatches(IEnumerable<FactoryInboundPallet> pallets, Func<string, Guid?> receiptFor) =>
        pallets.GroupBy(p => p.DispatchNoteId is Guid note ? FactoryDispatchKey(note) : FactoryPalletKey(p.PalletId))
            .Select(g =>
            {
                var rows = g.OrderBy(p => p.PalletCode, StringComparer.Ordinal).ToArray();
                var first = rows[0];
                var legacy = first.DispatchNoteId is null;
                return new FactoryInboundDispatch(g.Key, first.DispatchNoteId,
                    legacy ? first.PalletCode : first.DispatchCode ?? first.PalletCode, first.DestinationWarehouseId,
                    rows.Min(p => p.DispatchedAt), first.VehicleNumber, first.DriverName, rows,
                    legacy ? first.ReceiptDocumentId : receiptFor(g.Key));
            })
            .OrderBy(d => d.DispatchedAt).ThenBy(d => d.DispatchCode, StringComparer.Ordinal)
            .ToArray();

    public async Task<FactoryInboundDispatch[]> PendingFactoryDispatchesAsync(Guid actor, string role, CancellationToken ct)
    {
        var pallets = await PendingFactoryAsync(actor, role, ct);
        var keys = pallets.Where(p => p.DispatchNoteId.HasValue).Select(p => FactoryDispatchKey(p.DispatchNoteId!.Value)).Distinct().ToArray();
        var receipts = keys.Length == 0 ? new Dictionary<string, Guid>() : await Documents.AsNoTracking()
            .Where(d => keys.Contains(d.OperationKey)).ToDictionaryAsync(d => d.OperationKey, d => d.Id, ct);
        return GroupFactoryDispatches(pallets, key => receipts.TryGetValue(key, out var id) ? id : null);
    }

    /// <summary>Opens (or reopens) the single PNK that receives every pending pallet of one factory dispatch note.</summary>
    public Task<Guid> PrepareFactoryDispatchAsync(PrepareFactoryDispatchReceiptRequest request, Guid actor, string role, CancellationToken ct)
    {
        Keeper(role);
        return Atomic(async () =>
        {
            if (request.DispatchNoteId == Guid.Empty) throw PickingOperationException.Validation("Chọn phiếu xuất nhà máy cần nhận.");
            var key = FactoryDispatchKey(request.DispatchNoteId);
            var prior = await Documents.SingleOrDefaultAsync(d => d.OperationKey == key, ct);
            if (prior is not null)
            {
                await Access(prior.WarehouseId, actor, role, ct);
                if (prior.Kind != "Receipt" || prior.Reason != "Factory")
                    throw PickingOperationException.Conflict("Phiếu nhập không khớp phiếu xuất nhà máy.");
                return prior.Id;
            }
            var pallets = (await PendingFactoryAsync(actor, role, ct))
                .Where(p => p.DispatchNoteId == request.DispatchNoteId)
                .OrderBy(p => p.PalletCode, StringComparer.Ordinal).ToArray();
            if (pallets.Length == 0)
                throw PickingOperationException.NotFound("Không có pallet nào của phiếu xuất này đang chờ nhận tại kho được phân công.");
            var warehouseId = pallets[0].DestinationWarehouseId;
            if (pallets.Any(p => p.DestinationWarehouseId != warehouseId))
                throw PickingOperationException.Conflict("Các pallet của phiếu xuất không cùng kho nhận.");
            await Access(warehouseId, actor, role, ct);
            var first = pallets[0];
            var code = first.DispatchCode ?? request.DispatchNoteId.ToString("N");
            var document = New("Receipt", "Factory", warehouseId, key, request, actor, $"Nhận phiếu xuất {code} · {pallets.Length} pallet");
            Documents.Add(document);
            var expectation = new FactoryReceiptExpectation(request.DispatchNoteId, code, first.VehicleNumber, first.DriverName,
                pallets.Select(p => new FactoryExpectedPallet(p.PalletId, p.PalletCode, p.BatchCode, p.Sku, p.ProductName,
                    p.CartonCount, p.ExpirationDate)).ToArray());
            db.Add(new AuditLog { Id = Guid.NewGuid(), UserAccountId = actor, EntityName = "WarehouseDocument", EntityId = document.Id,
                Action = ExpectedPalletsAction, NewValues = JsonSerializer.Serialize(expectation), CreatedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(ct);
            return document.Id;
        }, ct);
    }

    /// <summary>Pallets a dispatch-note PNK must receive, each marked received or not; null for other receipts.</summary>
    public async Task<FactoryReceiptExpectation?> FactoryExpectationAsync(Guid id, Guid actor, string role, CancellationToken ct)
    {
        var d = await Find(id, ct);
        await Access(d.WarehouseId, actor, role, ct);
        return await ExpectationOf(d, ct);
    }

    private async Task<FactoryReceiptExpectation?> ExpectationOf(WarehouseDocument d, CancellationToken ct)
    {
        if (!d.OperationKey.StartsWith("factory-dispatch:", StringComparison.Ordinal)) return null;
        var json = await db.Set<AuditLog>().AsNoTracking()
            .Where(e => e.EntityName == "WarehouseDocument" && e.EntityId == d.Id && e.Action == ExpectedPalletsAction)
            .OrderBy(e => e.CreatedAt).Select(e => e.NewValues).FirstOrDefaultAsync(ct);
        var expectation = json is null ? null : JsonSerializer.Deserialize<FactoryReceiptExpectation>(json);
        if (expectation is null) throw PickingOperationException.Conflict("Phiếu nhập thiếu danh sách pallet của phiếu xuất nhà máy.");
        var ids = expectation.Pallets.Select(p => p.PalletId).ToArray();
        var received = await db.Set<FactoryPallet>().AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.Status == "Received" && p.ReceiptDocumentId == d.Id)
            .Select(p => new { p.Id, p.ReceivedAt, p.ReceiptInspectionJson })
            .ToDictionaryAsync(p => p.Id, ct);
        return expectation with
        {
            Pallets = expectation.Pallets.Select(p => received.TryGetValue(p.PalletId, out var r)
                ? p with { Received = true, ReceivedAt = r.ReceivedAt, Missing = IsWholePalletMissing(r.ReceiptInspectionJson) }
                : p with { Received = false, ReceivedAt = null, Missing = false }).ToArray(),
        };
    }

    /// <summary>True when every carton of the pallet was recorded "Missing" (the pallet never arrived).</summary>
    public static bool IsWholePalletMissing(string? inspectionJson)
    {
        if (string.IsNullOrWhiteSpace(inspectionJson)) return false;
        var rows = ReceiptInspection.Read(inspectionJson);
        return rows.Length > 0 && rows.All(r => r.Disposition == "Missing");
    }

    /// <summary>
    /// "Thiếu cả pallet": the pallet listed on the dispatch note did not arrive, so it cannot be scanned. Every carton is
    /// recorded Missing with the keeper's reason; nothing is added to stock and the result is acknowledged to the factory
    /// like any other receipt. The pallet then counts as handled, so the PNK can be completed.
    /// </summary>
    public Task<Guid> MarkPalletMissingAsync(Guid id, MissingPalletRequest request, Guid actor, string role, CancellationToken ct)
    {
        Keeper(role);
        return Atomic(async () =>
        {
            var d = await Find(id, ct);
            await Access(d.WarehouseId, actor, role, ct);
            if (d.Kind != "Receipt" || d.Reason != "Factory" || d.GoodsReceiptId != null || d.Status != "Open")
                throw PickingOperationException.Conflict("Chỉ đánh dấu thiếu pallet trên phiếu nhập nhà máy đang mở.");
            var reason = request.Reason?.Trim();
            if (string.IsNullOrEmpty(reason) || reason.Length > 300)
                throw PickingOperationException.Validation("Nhập lý do thiếu pallet (tối đa 300 ký tự).");
            var code = request.PalletCode?.Trim() ?? "";
            var expectation = await ExpectationOf(d, ct)
                ?? throw PickingOperationException.Conflict("Chỉ đánh dấu thiếu cả pallet trên phiếu nhập theo phiếu xuất nhà máy.");
            var pallet = expectation.Pallets.SingleOrDefault(p => p.PalletCode == code)
                ?? throw PickingOperationException.Conflict($"Pallet không thuộc phiếu xuất {expectation.DispatchCode}.");
            if (pallet.Received)
                throw PickingOperationException.Conflict("Pallet đã có kết quả nhận; không đánh dấu thiếu được nữa.");
            var manifest = await factory.ManifestAsync(code, ct);
            var inspection = manifest.Cartons.Select(c => new ReceiptCartonInspection(c.Code, "Missing", reason, null)).ToArray();
            var result = await ReceiveFactoryPallet(d, code, request.ExpectedVersion, actor, ct, inspection);
            db.Add(new AuditLog { Id = Guid.NewGuid(), EntityName = "WarehouseDocument", EntityId = id, UserAccountId = actor,
                Action = "ReceiptPalletMarkedMissing", CreatedAt = clock.GetUtcNow(),
                NewValues = JsonSerializer.Serialize(new { PalletCode = code, Reason = reason, CartonCount = inspection.Length }) });
            await db.SaveChangesAsync(ct);
            return result;
        }, ct);
    }

    /// <summary>A pallet-bound PNK accepts only its pallet; a dispatch-note PNK only the pallets listed on that note.</summary>
    private async Task RequireBoundPalletAsync(WarehouseDocument document, Guid palletId, CancellationToken ct)
    {
        RequireBoundPallet(document, palletId);
        var expectation = await ExpectationOf(document, ct);
        if (expectation is not null && expectation.Pallets.All(p => p.PalletId != palletId))
            throw PickingOperationException.Conflict($"Pallet không thuộc phiếu xuất {expectation.DispatchCode}; chỉ quét các pallet trong danh sách cần nhận.");
    }

    /// <summary>A dispatch-note PNK can be completed only after every listed pallet was received on it.</summary>
    private async Task RequireAllExpectedPalletsAsync(WarehouseDocument document, CancellationToken ct)
    {
        var expectation = await ExpectationOf(document, ct);
        if (expectation is null || expectation.AllReceived) return;
        var missing = expectation.Pallets.Where(p => !p.Received).Select(p => p.PalletCode).ToArray();
        throw PickingOperationException.Conflict($"Còn {missing.Length} pallet chưa nhận: {string.Join(", ", missing)}. Quét và xác nhận đủ pallet trước khi hoàn tất PNK.");
    }
}
