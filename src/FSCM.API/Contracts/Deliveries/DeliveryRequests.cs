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

public sealed record CreateDriverRequest(
    Guid WarehouseId,
    string? Code,
    string FullName,
    string Phone,
    string LicenseNumber);

public sealed record UpdateDriverRequest(
    string FullName,
    string Phone,
    string LicenseNumber);

public sealed record CreateVehicleRequest(
    Guid WarehouseId,
    string PlateNumber,
    string VehicleType,
    decimal Capacity);

public sealed record UpdateVehicleRequest(
    string VehicleType,
    decimal Capacity);

public sealed record SetActiveRequest(bool IsActive);
