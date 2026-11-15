using System.Text.Json;
using FSCM.Application.Features.Picking;
using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.WarehouseWork;

public sealed partial class WarehouseWorkService
{
    private async Task<WarehouseDocument> SnapshotIssue(WarehouseDocument source, Guid actor, CancellationToken ct)
    {
        if (source.Status is not ("Open" or "Prepared")) throw PickingOperationException.Conflict("Phiếu soạn đã đóng.");
        if (source.Reason == "Order" && source.Status != "Prepared")
            throw PickingOperationException.Conflict("Phiếu soạn đơn hàng chưa hoàn tất.");
        var lines = await Lines.Where(x => x.DocumentId == source.Id).ToArrayAsync(ct);
        var cartons = await Cartons.Where(x => x.DocumentId == source.Id).ToArrayAsync(ct);
        if (lines.Length == 0 || lines.Any(l => cartons.Count(c => c.LineId == l.Id && c.ScannedAt.HasValue) != l.Quantity))
            throw PickingOperationException.Conflict("Phải soạn đủ trước khi xác nhận xuất.");
        var issue = New("Issue", source.Reason, source.WarehouseId, $"issue:{source.Id:N}", new { source.Id }, actor, source.Note);
        issue.SourceDocumentId = source.Id; issue.DestinationWarehouseId = source.DestinationWarehouseId;
        issue.Status = source.Reason == "Order" ? "Prepared" : "Open";
        Documents.Add(issue);
        foreach (var original in lines)
        {
            var line = JsonSerializer.Deserialize<WarehouseDocumentLine>(JsonSerializer.Serialize(original))!;
            line.Id = Guid.NewGuid(); line.DocumentId = issue.Id; Lines.Add(line);
            foreach (var c in cartons.Where(c => c.LineId == original.Id))
                Cartons.Add(new() { Id = Guid.NewGuid(), DocumentId = issue.Id, LineId = line.Id,
                    CartonId = c.CartonId, LabelId = c.LabelId, ScannedAt = c.ScannedAt, ScannedByUserId = c.ScannedByUserId });
        }
        await db.SaveChangesAsync(ct);
        return issue;
    }

    public Task<Guid> RemoveCartonAsync(Guid id, string cartonCode, string? replacementCode, string reason,
        int version, Guid actor, string role, CancellationToken ct) => Atomic(async () =>
    {
        Keeper(role);
        var d = await Find(id, ct);
        await Access(d.WarehouseId, actor, role, ct);
        Version(d, version);
        if (d.Kind != "Picking" || d.Status is not ("Open" or "Prepared") ||
            await Documents.AnyAsync(x => x.SourceDocumentId == id && x.Kind == "Issue", ct))
            throw PickingOperationException.Conflict("Chỉ bỏ/đổi carton trên phiếu soạn chưa xuất.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 300)
            throw PickingOperationException.Validation("Nhập lý do, tối đa 300 ký tự.");
        var carton = await Cartons.SingleOrDefaultAsync(x => x.DocumentId == id && x.CartonId == cartonCode.Trim(), ct)
            ?? throw PickingOperationException.NotFound("Carton không nằm trong phiếu soạn.");
        var line = await Lines.SingleAsync(x => x.Id == carton.LineId, ct);
        var label = await db.BatchQrLabelRecords.SingleAsync(x => x.Id == carton.LabelId, ct);
        if (label.Status != "Picked" || label.CurrentWarehouseId != d.WarehouseId)
            throw PickingOperationException.Conflict("Carton không còn ở trạng thái đã soạn tại kho.");
        if (!string.IsNullOrWhiteSpace(replacementCode))
        {
            var replacement = await db.BatchQrLabelRecords.SingleOrDefaultAsync(x => x.QrCode == replacementCode.Trim(), ct);
            if (replacement is null || replacement.BatchId != line.BatchId || replacement.Status != "Available" ||
                replacement.CurrentWarehouseId != d.WarehouseId || replacement.Quantity != 1)
                throw PickingOperationException.Conflict("Carton thay thế phải khả dụng, cùng kho và batch allocation.");
        }
        if (line.PickingListId.HasValue)
        {
            var list = await db.PickingListRecords.SingleAsync(x => x.Id == line.PickingListId, ct);
            var shipment = await db.ShipmentRecords.SingleOrDefaultAsync(x => x.PickingListId == list.Id, ct);
            if (shipment is not null)
            {
                if (shipment.IssuedAt.HasValue || shipment.Status is not ("Ready" or "WaitingToPick"))
                    throw PickingOperationException.Conflict("Shipment đã chuyển bước; không được đổi carton.");
                shipment.Status = "WaitingToPick"; shipment.ReadyAt = null;
                shipment.TotalQuantity -= 1; shipment.Version++; shipment.UpdatedAt = clock.GetUtcNow();
            }
            var item = await db.PickingItemRecords.SingleAsync(x => x.Id == line.PickingItemId, ct);
            if (item.PickedQuantity < 1) throw PickingOperationException.Conflict("Số đã soạn không khớp carton.");
            item.PickedQuantity--; item.CompletedAt = null; item.Status = item.PickedQuantity == 0 ? "Pending" : "InProgress";
            list.Status = "InProgress"; list.ConfirmedAt = null; list.ConfirmedByUserId = null;
            list.Version++; list.UpdatedAt = clock.GetUtcNow();
        }
        label.Status = "Available"; label.Version++; label.UpdatedAt = clock.GetUtcNow();
        db.QrLabelEventRecords.Add(new() { Id = Guid.NewGuid(), BatchQrLabelId = label.Id,
            WarehouseId = d.WarehouseId, PerformedByUserId = actor, OperationKey = $"unpick:{id:N}:{d.Version}",
            EventType = "Unpicked", FromStatus = "Picked", ToStatus = "Available", Quantity = 1,
            ReferenceCode = d.Code, Note = reason.Trim(), OccurredAt = clock.GetUtcNow() });
        Cartons.Remove(carton); // Historical fact is retained in QR_LABEL_EVENT; this table is the active mapping.
        d.Status = "Open"; d.Version++;
        await db.SaveChangesAsync(ct);
        if (!string.IsNullOrWhiteSpace(replacementCode))
            return await ScanCore(id, replacementCode.Trim(), d.Version, actor, role, ct, line.Id);
        return id;
    }, ct);
}
