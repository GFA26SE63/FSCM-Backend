namespace FSCM.API.Contracts.Shipments;

public sealed record FlagShipmentExceptionRequest(string Reason, int ExpectedVersion);

public sealed record AssignShipmentRequest(Guid DriverId, Guid VehicleId, int ExpectedVersion);

public sealed record ConfirmShipmentReceiptLineRequest(
    Guid OrderLineId,
    Guid BatchId,
    decimal ReceivedQuantity,
    decimal DamagedQuantity);

public sealed record ConfirmShipmentReceiptRequest(
    IReadOnlyCollection<string> QrCodes,
    IReadOnlyCollection<ConfirmShipmentReceiptLineRequest> Lines,
    int ExpectedVersion,
    string? Note);
