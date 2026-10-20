// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class Delivery
{
    public Guid Id { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid DriverId { get; set; }
    public Guid VehicleId { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string DriverCodeSnapshot { get; set; } = string.Empty;
    public string DriverNameSnapshot { get; set; } = string.Empty;
    public string DriverPhoneSnapshot { get; set; } = string.Empty;
    public string DriverLicenseSnapshot { get; set; } = string.Empty;
    public string PlateNumberSnapshot { get; set; } = string.Empty;
    public string VehicleTypeSnapshot { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
