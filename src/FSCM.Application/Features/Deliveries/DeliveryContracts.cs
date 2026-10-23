namespace FSCM.Application.Features.Deliveries;

public static class DeliveryStatuses
{
    public const string Delivering = "Delivering";
    public const string Delivered = "Delivered";
}

public static class ReceiptConfirmationStatuses
{
    public const string Confirmed = "Confirmed";
    public const string Exception = "Exception";
}

public sealed record DispatchOrderData(
    Guid DriverId,
    Guid VehicleId,
    int ExpectedOrderVersion,
    string? Note);

public sealed record ConfirmRetailerReceiptData(
    IReadOnlyCollection<string> QrCodes,
    int ExpectedOrderVersion,
    string? Note);

public sealed record CreateDriverData(
    Guid WarehouseId,
    string? Code,
    string FullName,
    string Phone,
    string LicenseNumber);

public sealed record UpdateDriverData(
    string FullName,
    string Phone,
    string LicenseNumber);

public sealed record CreateVehicleData(
    Guid WarehouseId,
    string PlateNumber,
    string VehicleType,
    decimal Capacity);

public sealed record UpdateVehicleData(
    string VehicleType,
    decimal Capacity);

public sealed record SetActiveData(bool IsActive);

public sealed record DriverResult(
    Guid Id,
    Guid WarehouseId,
    string Code,
    string FullName,
    string Phone,
    string LicenseNumber,
    bool IsActive);

public sealed record VehicleResult(
    Guid Id,
    Guid WarehouseId,
    string PlateNumber,
    string VehicleType,
    decimal Capacity,
    bool IsActive);

public sealed record ReceiptItemResult(
    Guid Id,
    Guid BatchId,
    Guid QrLabelId,
    string QrCode,
    decimal ExpectedQuantity,
    decimal ReceivedQuantity,
    DateTimeOffset ScannedAt);

public sealed record ReceiptConfirmationResult(
    Guid Id,
    Guid ConfirmedByRetailerUserId,
    string Status,
    DateTimeOffset ConfirmedAt,
    string? Note,
    IReadOnlyCollection<ReceiptItemResult> Items);

public sealed record DeliveryResult(
    Guid Id,
    Guid OrderId,
    string OrderCode,
    Guid RetailerId,
    string RetailerCode,
    string RetailerName,
    string OrderStatus,
    int OrderVersion,
    Guid DriverId,
    string DriverCode,
    string DriverName,
    string DriverPhone,
    string DriverLicenseNumber,
    Guid VehicleId,
    string PlateNumber,
    string VehicleType,
    string Status,
    int Version,
    DateTimeOffset AssignedAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? DeliveredAt,
    string? Note,
    ReceiptConfirmationResult? Receipt);
