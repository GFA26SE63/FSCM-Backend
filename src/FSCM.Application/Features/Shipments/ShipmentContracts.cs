namespace FSCM.Application.Features.Shipments;

/// <summary>
/// F92 shipment lifecycle for a (sales order, warehouse) delivery part.
/// WaitingToPick → Ready (picking issued) → Issued (stock issued at dispatch, F96)
/// → Delivering (F58 assignment active) → Received (retailer confirmed in full).
/// Exception is a terminal-pending state when a part cannot be completed as-is.
/// </summary>
public static class ShipmentStatuses
{
    public const string WaitingToPick = "WaitingToPick";
    public const string Ready = "Ready";
    public const string Issued = "Issued";
    public const string Delivering = "Delivering";
    public const string Received = "Received";
    public const string Exception = "Exception";
}

public sealed record ShipmentLineResult(
    Guid OrderLineId,
    int LineNumber,
    Guid ProductId,
    string Sku,
    string ProductName,
    Guid BatchId,
    string BatchCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal NetAmount);

public sealed record ShipmentResult(
    Guid Id,
    Guid OrderId,
    string OrderCode,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    Guid PickingListId,
    string PickingCode,
    string ShipmentCode,
    string Status,
    decimal TotalQuantity,
    decimal ReceivedQuantity,
    Guid? DriverId,
    string? DriverCode,
    string? DriverName,
    string? DriverPhone,
    string? DriverLicenseNumber,
    Guid? VehicleId,
    string? PlateNumber,
    string? VehicleType,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? ReceivedAt,
    string? ExceptionReason,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<ShipmentLineResult> Lines);

public sealed record FlagShipmentExceptionData(string Reason, int ExpectedVersion);

public sealed record AssignShipmentData(Guid DriverId, Guid VehicleId, int ExpectedVersion);

/// <summary>F60 payment-status summary shown alongside a retailer's order delivery view.</summary>
public sealed record OrderPaymentStatusResult(
    string Method,
    string Status,
    decimal Amount,
    decimal AdjustedAmount,
    decimal ConfirmedAmount,
    decimal OutstandingAmount,
    DateOnly? DueDate,
    int DaysOverdue);

/// <summary>
/// F60: the retailer-facing delivery view of one order — its per-warehouse shipments (with the
/// assigned driver/vehicle from F58) plus the payment status. Prices on the shipment lines are the
/// locked per-batch amounts; nothing is recomputed from current discount tables.
/// </summary>
public sealed record RetailerOrderDeliveryResult(
    Guid OrderId,
    string OrderCode,
    IReadOnlyCollection<ShipmentResult> Shipments,
    OrderPaymentStatusResult? Payment);
