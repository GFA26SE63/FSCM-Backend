namespace FSCM.Application.Features.InternalTransfers;

public sealed record CreateInternalTransferLineData(Guid SourceBatchId, decimal Quantity, string? Note);
public sealed record CreateInternalTransferData(
    Guid SourceWarehouseId, Guid DestinationWarehouseId,
    IReadOnlyCollection<CreateInternalTransferLineData> Lines, string? Note);
public sealed record DispatchInternalTransferLineData(Guid TransferItemId, IReadOnlyCollection<string> QrCodes);
public sealed record DispatchInternalTransferData(int ExpectedVersion, IReadOnlyCollection<DispatchInternalTransferLineData> Lines);
public sealed record ReceiveInternalTransferData(int ExpectedVersion, IReadOnlyCollection<string> QrCodes);

public sealed record TransferSuggestionBatchResult(
    Guid BatchId, string BatchCode, DateOnly ExpirationDate, decimal AvailableQuantity, decimal SuggestedQuantity);
public sealed record InternalTransferSuggestionResult(
    Guid SourceWarehouseId, string SourceWarehouseCode, Guid DestinationWarehouseId,
    string DestinationWarehouseCode, Guid ProductId, string Sku, string ProductName,
    decimal MinimumStockQuantity, decimal DestinationAvailableQuantity, decimal SuggestedQuantity,
    IReadOnlyCollection<TransferSuggestionBatchResult> FefoBatches);

public sealed record InternalTransferLabelResult(Guid Id, string QrCode, decimal Quantity, DateTimeOffset DispatchedAt, DateTimeOffset? ReceivedAt);
public sealed record InternalTransferItemResult(
    Guid Id, Guid SourceBatchId, string SourceBatchCode, Guid? DestinationBatchId,
    string? DestinationBatchCode, Guid ProductId, string Sku, string ProductName,
    DateOnly ExpirationDate, decimal RequestedQuantity, decimal DispatchedQuantity,
    decimal ReceivedQuantity, string Status, string? Note,
    IReadOnlyCollection<InternalTransferLabelResult> Labels);
public sealed record InternalTransferEventResult(
    Guid Id, string OperationKey, string Action, string FromStatus, string ToStatus,
    Guid PerformedByUserId, string? Note, DateTimeOffset OccurredAt);
public sealed record InternalTransferResult(
    Guid Id, string TransferCode, Guid SourceWarehouseId, string SourceWarehouseCode,
    string SourceWarehouseName, Guid DestinationWarehouseId, string DestinationWarehouseCode,
    string DestinationWarehouseName, Guid CreatedByUserId, Guid? ApprovedByUserId,
    string Status, string? Note, string? RejectionReason, int Version,
    DateTimeOffset RequestedAt, DateTimeOffset? ApprovedAt, DateTimeOffset? DispatchedAt,
    DateTimeOffset? ReceivedAt, IReadOnlyCollection<InternalTransferItemResult> Items,
    IReadOnlyCollection<InternalTransferEventResult> Events);
