namespace FSCM.API.Contracts.Deliveries;

public sealed record DispatchOrderRequest(
    Guid DriverId,
    Guid VehicleId,
    int ExpectedOrderVersion,
    string? Note);

public sealed record ConfirmRetailerReceiptRequest(
    IReadOnlyCollection<string>? QrCodes,
    int? ExpectedOrderVersion,
    string? Note,
    IReadOnlyCollection<string>? LabelCodes = null,
    string? QrToken = null)
{
    public IReadOnlyCollection<string> ResolveQrCodes()
    {
        if (QrCodes is { Count: > 0 }) return QrCodes;
        if (LabelCodes is { Count: > 0 }) return LabelCodes;
        if (!string.IsNullOrWhiteSpace(QrToken)) return [QrToken];
        return [];
    }
}

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
