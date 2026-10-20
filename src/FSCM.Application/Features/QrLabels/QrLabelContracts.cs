namespace FSCM.Application.Features.QrLabels;

public sealed record GenerateQrLabelData(Guid BatchId, decimal Quantity, string? BlobUrl);
public sealed record SplitQrLabelData(decimal Quantity, int ExpectedVersion);
public sealed record ScanQrLabelData(
    string Action, Guid WarehouseId, Guid? DestinationBatchId, string? ReferenceCode,
    string? Note, int ExpectedVersion);

public sealed record QrLabelResult(
    Guid Id, string QrCode, string QrPayload, Guid BatchId, string BatchCode,
    Guid WarehouseId, string WarehouseCode, Guid ProductId, string Sku,
    decimal Quantity, string Status, Guid? ParentLabelId, string? ParentQrCode,
    Guid RootLabelId, string RootQrCode, string? BlobUrl, int Version,
    DateTimeOffset GeneratedAt, DateTimeOffset UpdatedAt, DateTimeOffset? InvalidatedAt);

public sealed record QrLabelEventResult(
    Guid Id, string OperationKey, string EventType, Guid WarehouseId, string WarehouseCode,
    string FromStatus, string ToStatus, decimal Quantity, string? ReferenceCode,
    string? Note, Guid PerformedByUserId, DateTimeOffset OccurredAt);

public sealed record QrLabelDetailResult(
    QrLabelResult Label, IReadOnlyCollection<QrLabelResult> Lineage,
    IReadOnlyCollection<QrLabelEventResult> Events);

/// <summary>A printable TEM label artifact for a single carton/label.</summary>
public sealed record QrLabelPrintResult(
    string QrCode, string BatchCode, string Sku, string ProductName,
    string WarehouseCode, string WarehouseName, decimal Quantity,
    DateOnly ExpirationDate, string QrSvg);

/// <summary>A printable document bundling one or more TEM labels ready for a label printer.</summary>
public sealed record QrLabelPrintDocument(
    string ContentType, string FileName, string Html,
    IReadOnlyCollection<QrLabelPrintResult> Labels);
