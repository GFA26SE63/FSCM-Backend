namespace FSCM.API.Contracts.Picking;

public sealed record ScanPickingLabelRequest(
    string QrCode,
    decimal? Quantity,
    int ExpectedPickingVersion,
    int ExpectedLabelVersion,
    IReadOnlyCollection<RunPickingSplitQuantityRequest> Splits);

public sealed record RunPickingSplitQuantityRequest(Guid AllocationRunAllocationId, decimal Quantity);

public sealed record CompleteRunPickingRequest(
    int ExpectedPickingVersion,
    string? ExceptionType,
    string? Note);

