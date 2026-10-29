// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

/// <summary>
/// A delivery part (F92) for one (sales order, warehouse) pair. Created once the warehouse's run
/// picking list is Ready; since a run picking list spans several orders, each list produces one
/// shipment per order. It tracks that slice of the order through its own lifecycle and aggregates
/// the picked quantity/line-batch-price linkage. Driver/vehicle assignment (F58) and actual
/// issue/receipt are layered on top; the owning order is only "Delivered" when every shipment part
/// is fully received.
/// </summary>
public sealed class Shipment
{
    public Guid Id { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid WarehouseId { get; set; }

    /// <summary>The warehouse run picking list (F92/run-picking) this part was created from.</summary>
    public Guid RunPickingListId { get; set; }

    public string ShipmentCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    /// <summary>Driver assigned for this delivery part (F58). Null until an Operator assigns one.</summary>
    public Guid? DriverId { get; set; }

    /// <summary>Vehicle assigned for this delivery part (F58). Null until an Operator assigns one.</summary>
    public Guid? VehicleId { get; set; }

    public string? DriverCodeSnapshot { get; set; }
    public string? DriverNameSnapshot { get; set; }
    public string? DriverPhoneSnapshot { get; set; }
    public string? DriverLicenseSnapshot { get; set; }
    public string? PlateNumberSnapshot { get; set; }
    public string? VehicleTypeSnapshot { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }

    /// <summary>Total picked carton quantity aggregated from the warehouse's picking items.</summary>
    public decimal TotalQuantity { get; set; }

    /// <summary>Total actually received carton quantity (F92 aggregation; partial receipt never auto-completes).</summary>
    public decimal ReceivedQuantity { get; set; }

    public DateTimeOffset? ReadyAt { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public string? ExceptionReason { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
