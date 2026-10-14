namespace FSCM.API.Contracts.InternalTransfers;

public sealed record CreateInternalTransferLineRequest(Guid SourceBatchId, decimal Quantity, string? Note);
public sealed record CreateInternalTransferRequest(
    Guid SourceWarehouseId, Guid DestinationWarehouseId,
    IReadOnlyCollection<CreateInternalTransferLineRequest> Lines, string? Note);
public sealed record TransferDecisionRequest(int ExpectedVersion);
public sealed record RejectInternalTransferRequest(int ExpectedVersion, string Reason);
public sealed record DispatchInternalTransferLineRequest(Guid TransferItemId, IReadOnlyCollection<string> QrCodes);
public sealed record DispatchInternalTransferRequest(int ExpectedVersion, IReadOnlyCollection<DispatchInternalTransferLineRequest> Lines);
public sealed record ReceiveInternalTransferRequest(int ExpectedVersion, IReadOnlyCollection<string> QrCodes);
