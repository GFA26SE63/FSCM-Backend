// Generated from the approved FSCM physical ERD. Review schema changes through EF migrations.

namespace FSCM.Domain.Entities;

public sealed class PickingList
{
    public Guid Id { get; set; }
    public string PickingCode { get; set; } = string.Empty;
    public Guid SalesOrderId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public Guid? ConfirmedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

