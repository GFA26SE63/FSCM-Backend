using FSCM.Application.Features.Picking;
using FSCM.Application.Features.Shipments;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FSCM.Persistence.WarehouseWork;

public sealed partial class WarehouseWorkService
{
    public Task<bool> AssignTransferTransportAsync(Guid id, Guid driverId, Guid vehicleId, int expectedVersion,
        Guid actor, string role, CancellationToken ct) => Atomic(async () =>
    {
        if (role is not (SystemRoles.Admin or SystemRoles.Operator))
            throw PickingOperationException.Forbidden("Chỉ Admin hoặc Operator được gán xe và tài xế.");
        var transfer = await db.Set<WarehouseTransfer>().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw PickingOperationException.NotFound("Không tìm thấy điều phối luân chuyển.");
        if (!transfer.SourceIssueDocumentId.HasValue || transfer.Status != "Approved")
            throw PickingOperationException.Conflict("Điều phối không ở trạng thái chờ gán xe và tài xế.");
        if (transfer.Version != expectedVersion)
            throw PickingOperationException.Conflict("Điều phối đã thay đổi; tải lại và thử lại.");
        var driver = await db.Set<Driver>().SingleOrDefaultAsync(x => x.Id == driverId, ct)
            ?? throw PickingOperationException.NotFound("Không tìm thấy tài xế.");
        var vehicle = await db.Set<Vehicle>().SingleOrDefaultAsync(x => x.Id == vehicleId, ct)
            ?? throw PickingOperationException.NotFound("Không tìm thấy xe.");
        if (!driver.IsActive || !vehicle.IsActive || driver.WarehouseId != transfer.SourceWarehouseId || vehicle.WarehouseId != transfer.SourceWarehouseId)
            throw PickingOperationException.Conflict("Xe và tài xế phải đang hoạt động tại kho xuất.");
        var quantity = await db.Set<WarehouseTransferItem>().Where(x => x.WarehouseTransferId == id).SumAsync(x => x.DispatchedQuantity, ct);
        if (vehicle.Capacity < quantity)
            throw PickingOperationException.Conflict("Sức chứa xe không đủ cho số carton cần luân chuyển.");
        var busy = await db.Set<WarehouseTransfer>().AnyAsync(x => x.Id != id && x.Status == "InTransit" &&
            (x.DriverId == driverId || x.VehicleId == vehicleId), ct) ||
            await db.ShipmentRecords.AnyAsync(x => x.Status == ShipmentStatuses.Delivering &&
                (x.DriverId == driverId || x.VehicleId == vehicleId), ct);
        if (busy) throw PickingOperationException.Conflict("Xe hoặc tài xế đang thực hiện chuyến khác.");
        var now = clock.GetUtcNow();
        transfer.DriverId = driver.Id; transfer.VehicleId = vehicle.Id;
        transfer.DriverCodeSnapshot = driver.Code; transfer.DriverNameSnapshot = driver.FullName;
        transfer.PlateNumberSnapshot = vehicle.PlateNumber; transfer.AssignedAt = now;
        transfer.DispatchedAt = now; transfer.Status = "InTransit"; transfer.Version++; transfer.UpdatedAt = now;
        db.Set<WarehouseTransferEvent>().Add(new() { Id = Guid.NewGuid(), WarehouseTransferId = id,
            PerformedByUserId = actor, OperationKey = $"wd-assign:{id:N}:{expectedVersion}", Action = "Assigned",
            FromStatus = "Approved", ToStatus = "InTransit", Note = "Đã gán xe và tài xế.", OccurredAt = now });
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    private async Task EnsureTransferMayReceiveAsync(WarehouseDocument issue, CancellationToken ct)
    {
        var transfer = await db.Set<WarehouseTransfer>().SingleOrDefaultAsync(x => x.SourceIssueDocumentId == issue.Id, ct)
            ?? throw PickingOperationException.Conflict("PXK chưa tạo điều phối luân chuyển.");
        if (transfer.Status != "InTransit")
            throw PickingOperationException.Conflict("Phải gán xe và tài xế trước khi mở PNK luân chuyển.");
    }

    private async Task CreateTransferDispatchAsync(WarehouseDocument issue, WarehouseDocumentLine[] lines,
        WarehouseDocumentCarton[] cartons, Guid actor, CancellationToken ct)
    {
        if (await db.Set<WarehouseTransfer>().AnyAsync(x => x.SourceIssueDocumentId == issue.Id, ct)) return;
        var now = clock.GetUtcNow();
        var transfer = new WarehouseTransfer { Id = Guid.NewGuid(), SourceIssueDocumentId = issue.Id,
            TransferCode = $"DPL-{issue.Id:N}"[..20], SourceWarehouseId = issue.WarehouseId,
            DestinationWarehouseId = issue.DestinationWarehouseId!.Value, CreatedByUserId = actor,
            ApprovedByUserId = actor, Status = "Approved", Note = issue.Note, Version = 1,
            RequestedAt = now, ApprovedAt = now, CreatedAt = now, UpdatedAt = now };
        db.Set<WarehouseTransfer>().Add(transfer);
        foreach (var line in lines)
        {
            var item = new WarehouseTransferItem { Id = Guid.NewGuid(), WarehouseTransferId = transfer.Id,
                SourceBatchId = line.BatchId!.Value, RequestedQuantity = line.Quantity,
                DispatchedQuantity = line.Quantity, Status = "Approved", Note = issue.Note,
                CreatedAt = now, UpdatedAt = now };
            db.Set<WarehouseTransferItem>().Add(item);
            foreach (var carton in cartons.Where(x => x.LineId == line.Id))
                db.Set<WarehouseTransferLabel>().Add(new() { Id = Guid.NewGuid(), WarehouseTransferItemId = item.Id,
                    BatchQrLabelId = carton.LabelId!.Value, Quantity = 1, DispatchedAt = now });
        }
        db.Set<WarehouseTransferEvent>().Add(new() { Id = Guid.NewGuid(), WarehouseTransferId = transfer.Id,
            PerformedByUserId = actor, OperationKey = $"wd-post:{issue.Id:N}", Action = "Created",
            FromStatus = "", ToStatus = "Approved", Note = "PXK đã ghi sổ; chờ gán xe và tài xế.", OccurredAt = now });
    }

    private async Task CompleteTransferDispatchAsync(Guid issueId, Guid actor, CancellationToken ct)
    {
        var transfer = await db.Set<WarehouseTransfer>().SingleOrDefaultAsync(x => x.SourceIssueDocumentId == issueId, ct)
            ?? throw PickingOperationException.Conflict("Không tìm thấy điều phối luân chuyển.");
        if (transfer.Status != "InTransit")
            throw PickingOperationException.Conflict("Điều phối chưa ở trạng thái đang vận tải.");
        var now = clock.GetUtcNow();
        transfer.Status = "Received"; transfer.ReceivedAt = now; transfer.Version++; transfer.UpdatedAt = now;
        foreach (var item in await db.Set<WarehouseTransferItem>().Where(x => x.WarehouseTransferId == transfer.Id).ToArrayAsync(ct))
        { item.ReceivedQuantity = item.DispatchedQuantity; item.Status = "Received"; item.UpdatedAt = now; }
        db.Set<WarehouseTransferEvent>().Add(new() { Id = Guid.NewGuid(), WarehouseTransferId = transfer.Id,
            PerformedByUserId = actor, OperationKey = $"wd-receive:{issueId:N}", Action = "Received",
            FromStatus = "InTransit", ToStatus = "Received", Note = "PNK đã ghi sổ và cập nhật tồn kho đích.", OccurredAt = now });
    }
}
