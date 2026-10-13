using FSCM.Modules.Ordering.Domain.Orders;

namespace FSCM.Modules.Ordering.Application;

public sealed record OrderLineInput(string Sku, int Quantity);
/// <param name="Payment">COD, Prepaid or Credit.</param>
public sealed record CreateOrderInput(string RetailerCode, IReadOnlyList<OrderLineInput> Lines, string Payment, int CreditTermDays = 0, DateOnly? PromisedDate = null, string? Note = null);
/// <summary>An order created on a Sales phone without connectivity. The UUID is the idempotency key; LocalCode is OFF-{device}-{0000}.</summary>
public sealed record OfflineOrderInput(Guid ClientRequestId, string DeviceCode, string LocalCode, DateTime CreatedOnDeviceUtc, CreateOrderInput Order);
/// <param name="Status">Created, Rejected (stock gone / out of area / credit not allowed), Duplicate (already synced), Failed (invalid data).</param>
public sealed record SyncResultItem(Guid ClientRequestId, string LocalCode, string Status, string? OrderCode, string? OrderStatus, string? Message);

public sealed record OrderCreatedDto(string Code, string Status, decimal GrossAmount, decimal DiscountAmount, decimal NetAmount, decimal EstimatedDeliveryCost);

public sealed record AllocationDto(string BatchCode, string Warehouse, int Quantity, decimal DistanceKm, int TransitDays, DateOnly Eta, int RemainingDays, decimal Score,
    decimal TripCost, decimal BatchDiscountPercent, decimal BatchDiscountAmount);
public sealed record OrderLineDto(int LineNo, string Sku, string Name, int Quantity, decimal UnitPrice, decimal GrossAmount, string? PromotionCode, decimal PromotionPercent,
    decimal PromotionAmount, decimal BatchDiscountAmount, decimal NetAmount, IReadOnlyList<AllocationDto> Allocations);
public sealed record StatusEntryDto(int Seq, string Status, DateTime AtUtc, string By, string? Note);
public sealed record DeliveryCostPart(string Warehouse, decimal Km, decimal Cost);
public sealed record DeliveryDto(string? Plate, string? VehicleType, string? DriverCode, string? DriverName, string? DriverPhone, DateTime? DispatchedAtUtc);

public sealed record OrderSummaryDto(string Code, string RetailerCode, string RetailerName, string Status, string Payment, int CreditTermDays, decimal NetAmount,
    DateTime CreatedAtUtc, string? SalesUserName, string Source, int LineCount, int TotalCases);

public sealed record OrderDetailDto(string Code, string RetailerCode, string RetailerName, string Status, string Payment, int CreditTermDays, string Source, string? LocalCode,
    string? SalesUserId, string? SalesUserName, string CreatedByName, DateTime CreatedAtUtc, DateOnly? PromisedDate, string? Note, string? RejectReason,
    decimal GrossAmount, decimal PromotionAmount, decimal BatchDiscountAmount, decimal NetAmount,
    IReadOnlyList<OrderLineDto> Lines, IReadOnlyList<StatusEntryDto> History, DeliveryDto Delivery,
    decimal EstimatedDeliveryCost, IReadOnlyList<DeliveryCostPart> DeliveryCostParts, IReadOnlyList<string> PickingLists,
    string? ReceiptQr);

public sealed record ComplaintInput(string OrderCode, string Content);
public sealed record ComplaintDto(string Code, string OrderCode, string RetailerCode, string Content, string Status, string? Result, DateTime CreatedAtUtc, DateTime? ResolvedAtUtc);
public sealed record ResolveComplaintInput(string Result);

public sealed record PickingLineDto(int LineNo, string BatchCode, string Sku, int Quantity, int Picked);
public sealed record PickingListDto(string Code, string OrderCode, string Warehouse, string Status, DateTime CreatedAtUtc, IReadOnlyList<PickingLineDto> Lines, int ScanCount);
public sealed record ScanInput(string LabelCode, int? Quantity = null);
public sealed record DispatchInput(string Plate, string DriverCode);
public sealed record ConfirmReceiptInput(string QrToken);
public sealed record RejectInput(string Reason);
public sealed record AllocationSettingsDto(decimal WE, decimal WD, decimal WQ, decimal DmaxRetailerKm);
