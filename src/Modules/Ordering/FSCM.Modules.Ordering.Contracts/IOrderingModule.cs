using FSCM.BuildingBlocks.Application;

namespace FSCM.Modules.Ordering.Contracts;

/// <summary>Cases issued from one warehouse to one retailer for one SKU, used by Inventory to estimate sales velocity.</summary>
/// <param name="Cases">Cases issued inside the requested window.</param>
/// <param name="FirstIssueDate">Earliest issue of this SKU from this warehouse to this retailer, ever (tells whether there is enough history).</param>
public sealed record OutboundRow(string RetailerCode, int Cases, DateOnly FirstIssueDate);

public interface IOrderingModule
{
    Task<IReadOnlyList<OutboundRow>> OutboundByRetailerAsync(string warehouse, string sku, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public sealed record OrderLineInfo(string Sku, int Quantity, decimal GrossAmount, decimal DiscountAmount, decimal NetAmount);

// ---- integration events (SPEC §4). SalesUserId is only set when the order was closed (created) by a Sales. ----
public sealed record OrderSubmitted(string OrderCode, string RetailerCode, string RetailerName, string? SalesUserId, decimal NetAmount, bool Offline) : IntegrationEvent;
public sealed record OrderApproved(string OrderCode, string RetailerCode, string? SalesUserId) : IntegrationEvent;
public sealed record OrderRejected(string OrderCode, string RetailerCode, string? SalesUserId, string Reason, bool StockWasHeld) : IntegrationEvent;
public sealed record OrderCancelled(string OrderCode, string RetailerCode, string? SalesUserId, string CancelledBy, bool StockWasHeld) : IntegrationEvent;
public sealed record OrderShipped(string OrderCode, string RetailerCode, string? SalesUserId, string Plate, string DriverName, string DriverPhone) : IntegrationEvent;

/// <summary>Retailer scanned the QR on receipt. Finance creates the receivable (credit), Loyalty gives points.</summary>
public sealed record OrderDelivered(string OrderCode, string RetailerCode, string? SalesUserId, string PaymentMethod, int CreditTermDays,
    decimal GrossAmount, decimal DiscountAmount, decimal NetAmount, DateOnly DeliveredOn, IReadOnlyList<OrderLineInfo> Lines) : IntegrationEvent;

/// <summary>Delivered and, for credit orders, paid. Performance builds KPI / ranking from this event (SalesUserId null = not closed by a Sales).</summary>
public sealed record OrderCompleted(string OrderCode, string RetailerCode, string? SalesUserId, string PaymentMethod,
    decimal GrossAmount, decimal DiscountAmount, decimal NetAmount, DateOnly CompletedOn, IReadOnlyList<OrderLineInfo> Lines) : IntegrationEvent;

public sealed record ComplaintCreated(string ComplaintCode, string OrderCode, string RetailerCode) : IntegrationEvent;
