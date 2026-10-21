namespace FSCM.API.Contracts.Picking;

public sealed record ScanPickingLabelRequest(
    string QrCode,
    decimal? Quantity,
    int ExpectedPickingVersion,
    int ExpectedLabelVersion);

