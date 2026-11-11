using FSCM.Domain.Entities;

namespace FSCM.Application.Features.QrLabels;

/// <summary>A retired source keeps its original quantity; only its two children represent stock.</summary>
public static class QrLabelSplit
{
    public static (BatchQrLabel Taken, BatchQrLabel Replacement) Create(
        BatchQrLabel source, decimal takenQuantity, string takenCode, string replacementCode,
        string takenStatus, DateTimeOffset now)
    {
        if (source.Status != QrLabelStatuses.Available)
            throw QrLabelOperationException.Conflict("Only an available source label can be split.");
        if (takenQuantity <= 0 || decimal.Truncate(takenQuantity) != takenQuantity || takenQuantity >= source.Quantity)
            throw QrLabelOperationException.Validation("Split quantity must be a positive whole number below the source quantity.");
        BatchQrLabel Child(string code, decimal quantity, string status) => new()
        {
            Id = Guid.NewGuid(), BatchId = source.BatchId, CurrentWarehouseId = source.CurrentWarehouseId,
            GoodsReceiptId = source.GoodsReceiptId, ParentLabelId = source.Id,
            RootLabelId = source.RootLabelId ?? source.Id, QrCode = code, Quantity = quantity,
            Status = status, Version = 1, GeneratedAt = now, UpdatedAt = now
        };
        var taken = Child(takenCode, takenQuantity, takenStatus);
        var replacement = Child(replacementCode, source.Quantity - takenQuantity, QrLabelStatuses.Available);
        source.Status = QrLabelStatuses.Split;
        source.InvalidatedAt = now;
        source.Version++;
        source.UpdatedAt = now;
        return (taken, replacement);
    }
}
