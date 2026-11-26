// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class WarehouseTransfer
{
    public Guid Id { get; set; }
    public Guid? SourceIssueDocumentId { get; set; }
    public string TransferCode { get; set; } = string.Empty;
    public Guid SourceWarehouseId { get; set; }
    public Guid DestinationWarehouseId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public Guid? DriverId { get; set; }
    public Guid? VehicleId { get; set; }
    public string? DriverCodeSnapshot { get; set; }
    public string? DriverNameSnapshot { get; set; }
    public string? PlateNumberSnapshot { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? RejectionReason { get; set; }
    public int Version { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
