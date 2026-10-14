namespace FSCM.API.Contracts.QrLabels;

public sealed record GenerateQrLabelRequest(Guid BatchId, decimal Quantity, string? BlobUrl);
public sealed record SplitQrLabelRequest(decimal Quantity, int ExpectedVersion);
public sealed record ScanQrLabelRequest(
    string Action, Guid WarehouseId, Guid? DestinationBatchId,
    string? ReferenceCode, string? Note, int ExpectedVersion);
