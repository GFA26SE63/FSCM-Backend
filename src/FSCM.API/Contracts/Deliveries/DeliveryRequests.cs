namespace FSCM.API.Contracts.Deliveries;

public sealed record DispatchOrderRequest(
    Guid DriverId,
    Guid VehicleId,
    int ExpectedOrderVersion,
    string? Note);

public sealed record ConfirmRetailerReceiptRequest(
    IReadOnlyCollection<string> QrCodes,
    int ExpectedOrderVersion,
    string? Note);
