namespace FSCM.API.Contracts.Shipments;

public sealed record FlagShipmentExceptionRequest(string Reason, int ExpectedVersion);

public sealed record AssignShipmentRequest(Guid DriverId, Guid VehicleId, int ExpectedVersion);
